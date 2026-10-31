using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Systems;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.UI.Elements;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.UI;
using Terraria.ObjectData;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.UI
{
    /// <summary>
    /// 统计主窗口，负责九个统计 Tab 的导航、响应式布局以及各页内容的装载。
    /// </summary>
    internal sealed class StatisticsWindowState : UIState
    {
        private const int TabCount = 9;
        private const float WindowMargin = 12f;
        private const float WindowPadding = 12f;
        private const float PreferredWindowWidth = 1100f;
        private const float PreferredWindowHeight = 820f;
        private const float WindowWidthViewportRatio = 0.9f;
        private const float WindowHeightViewportRatio = 0.94f;
        private const float HeaderHeight = 40f;
        private const float ColumnGap = 10f;
        private const float TabGap = 4f;
        private const int BlocksTabIndex = 0;
        private const int ItemsTabIndex = 1;
        private const int AmmoTabIndex = 2;
        private const int InteractionsTabIndex = 3;
        private const int KillsTabIndex = 4;
        private const int EconomyTabIndex = 5;
        private const int FishingTabIndex = 6;
        private const int MovementTabIndex = 7;
        private const int SurvivalTabIndex = 8;
        private const float ToolbarTop = 38f;
        private const float ToolbarHeight = 28f;
        private const float ListTop = 74f;
        private const float ExpandCollapseButtonWidth = 94f;
        private const float CompatibilityButtonWidth = 26f;
        private const float CompatibilityButtonGap = 6f;

        // 顺序同时决定导航显示顺序，键名用于拼接各 Tab 的本地化路径。
        private static readonly string[] TabLocalizationKeys =
        {
            "Blocks",
            "Items",
            "Ammo",
            "Interactions",
            "Kills",
            "Economy",
            "Fishing",
            "Movement",
            "Survival"
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
        private CompatibilityInfoButton compatibilityInfoButton;
        private StatisticsToolbar toolbar;
        private string selectedBlockSubTab = "Tiles";
        private readonly Dictionary<string, StatisticsSubTabBar> subTabBars = new();
        private VirtualizedStatisticsList blockList => statisticsLists["Blocks/" + selectedBlockSubTab];
        private VirtualizedStatisticsList itemList;
        private VirtualizedStatisticsList ammoList;
        private VirtualizedStatisticsList interactionList;
        private VirtualizedStatisticsList killList;
        private VirtualizedStatisticsList economyList;
        private string selectedFishingSubTab = "Catches";
        private VirtualizedStatisticsList fishingList => statisticsLists["Fishing/" + selectedFishingSubTab];
        private VirtualizedStatisticsList baitList;
        private VirtualizedStatisticsList movementList;
        private VirtualizedStatisticsList survivalList;
        private Dictionary<string, VirtualizedStatisticsList> statisticsLists;
        private UIImageButton closeButton;
        private int selectedTabIndex;
        private Point lastViewportSize;
        private bool isBlockListAttached;
        private bool isItemListAttached;
        private bool isAmmoListAttached;
        private bool isInteractionListAttached;
        private bool isKillListAttached;
        private bool isEconomyListAttached;
        private bool isFishingListAttached;
        private bool isBaitListAttached;
        private bool isMovementListAttached;
        private bool isSurvivalListAttached;
        private bool runtimeStatisticsAccessEnabled;

        internal bool IsWritingSearchText => toolbar?.IsWritingSearchText == true;

        private bool CanReadRuntimeStatistics => runtimeStatisticsAccessEnabled
            && !Main.gameMenu
            && Main.LocalPlayer?.active == true;

        /// <summary>
        /// 开始新的窗口会话并清除旧人物的内存视图；当前人物的持久化视图随后重新加载。
        /// </summary>
        internal void ResetTransientViewState()
        {
            if (statisticsLists == null)
            {
                return;
            }

            foreach (VirtualizedStatisticsList statisticsList in statisticsLists.Values)
            {
                statisticsList.ResetTransientState();
            }
        }

        /// <summary>
        /// 在窗口真正打开后启用角色数据访问，并刷新默认或上次选中的统计页。
        /// </summary>
        internal void BeginRuntimeStatisticsAccess()
        {
            runtimeStatisticsAccessEnabled = true;
            foreach (var page in StatisticsSubTabs.For("Blocks")) statisticsLists[page.PageKey].Remove();
            isBlockListAttached = false;
            var data = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>().Data;
            data.SelectedSubTabs.TryGetValue("Blocks", out string savedSubTab);
            selectedBlockSubTab = StatisticsSubTabs.Resolve("Blocks", savedSubTab);
            foreach (var page in StatisticsSubTabs.For("Fishing")) statisticsLists[page.PageKey].Remove();
            isFishingListAttached = false;
            isBaitListAttached = false;
            data.SelectedSubTabs.TryGetValue("Fishing", out string savedFishingSubTab);
            selectedFishingSubTab = StatisticsSubTabs.Resolve("Fishing", savedFishingSubTab);
            toolbar.RefreshLocalizedText();
            UpdateWindowHeaderLayout();
            SelectTab(selectedTabIndex, playSound: false, saveCurrent: false);
        }

        /// <summary>
        /// 在窗口关闭前停止访问角色数据，避免世界卸载或模组加载阶段读取未就绪的 ModPlayer 数组。
        /// </summary>
        internal void EndRuntimeStatisticsAccess()
        {
            SaveActiveExpansionState();
            SaveActiveListViewState();
            runtimeStatisticsAccessEnabled = false;
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

            compatibilityInfoButton = new CompatibilityInfoButton();
            compatibilityInfoButton.Left.Set(-ExpandCollapseButtonWidth - CompatibilityButtonGap - CompatibilityButtonWidth, 1f);
            compatibilityInfoButton.Top.Set(0f, 0f);
            compatibilityInfoButton.Width.Set(CompatibilityButtonWidth, 0f);
            compatibilityInfoButton.Height.Set(28f, 0f);

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

            ammoList = new VirtualizedStatisticsList(
                CreateAmmoGroups(data: null),
                StatisticsMetricDisplay.AmmoActivity);
            ammoList.Top.Set(ListTop, 0f);
            ammoList.Width.Set(0f, 1f);
            ammoList.Height.Set(-ListTop, 1f);

            interactionList = new VirtualizedStatisticsList(
                CreateInteractionGroups(data: null),
                StatisticsMetricDisplay.Events);
            interactionList.Top.Set(ListTop, 0f);
            interactionList.Width.Set(0f, 1f);
            interactionList.Height.Set(-ListTop, 1f);

            killList = new VirtualizedStatisticsList(
                CreateKillGroups(characterData: null, worldData: null),
                StatisticsMetricDisplay.KillOutcomes);
            killList.Top.Set(ListTop, 0f);
            killList.Width.Set(0f, 1f);
            killList.Height.Set(-ListTop, 1f);

            economyList = new VirtualizedStatisticsList(
                CreateEconomyGroups(data: null),
                StatisticsMetricDisplay.EconomyTransactions);
            economyList.Top.Set(ListTop, 0f);
            economyList.Width.Set(0f, 1f);
            economyList.Height.Set(-ListTop, 1f);

            baitList = new VirtualizedStatisticsList(
                CreateBaitGroups(data: null),
                StatisticsMetricDisplay.AmmoActivity);
            baitList.Top.Set(ListTop, 0f);
            baitList.Width.Set(0f, 1f);
            baitList.Height.Set(-ListTop, 1f);

            // 页面身份与导航位置解耦，子页拥有独立的列表及持久化视图。
            statisticsLists = new Dictionary<string, VirtualizedStatisticsList>
            {
                ["Items"] = itemList,
                ["Ammo"] = ammoList,
                ["Interactions"] = interactionList,
                ["Kills"] = killList,
                ["Economy"] = economyList,
                ["Fishing/Bait"] = baitList,
                ["Movement"] = movementList,
                ["Survival"] = survivalList
            };
            foreach (string mainTab in TabLocalizationKeys)
            {
                var pages = StatisticsSubTabs.For(mainTab);
                if (pages.Count == 0) continue;
                foreach (var page in pages)
                {
                    if (mainTab == "Fishing" && page.Id == "Bait") continue;
                    var list = mainTab == "Fishing"
                        ? new VirtualizedStatisticsList(page.Id == "Casts" ? FishingCastStatisticsList.CreateGroups(null) : CreateFishingGroups(null),
                            page.Id == "Casts" ? StatisticsMetricDisplay.Events : StatisticsMetricDisplay.FishingActivity)
                        : new VirtualizedStatisticsList(CreateBlockPageGroups(null, page.Id),
                            page.Id == "Liquids" ? StatisticsMetricDisplay.LiquidVolume : StatisticsMetricDisplay.WiringActivity);
                    list.Width.Set(0f, 1f);
                    statisticsLists[page.PageKey] = list;
                }
                var bar = new StatisticsSubTabBar(pages, id => SelectSubTab(mainTab, id));
                bar.Top.Set(ToolbarTop + ToolbarHeight + 6f, 0f);
                bar.LayoutChanged += UpdateListLayout;
                subTabBars[mainTab] = bar;
            }

            foreach (VirtualizedStatisticsList statisticsList in statisticsLists.Values)
            {
                statisticsList.ExpansionStateChanged += UpdateExpandCollapseButton;
                statisticsList.ExpansionStateCommitted += SaveActiveExpansionState;
                statisticsList.ViewStateCommitted += SaveActiveListViewState;
            }

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

            // UIState 在 ModSystem.Load 中会预激活一次。此处只刷新不依赖角色运行时
            // 数据的界面文本；真实统计由 BeginRuntimeStatisticsAccess 在窗口打开后启用。
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

            base.Update(gameTime);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
            if (!CanReadRuntimeStatistics) return;

            // 原版 InterfaceLogic3 会在 Update 之后清空 hoverItemName。窗口及其子元素
            // 绘制完成后使用专用提示接口，既避免被清空，也避免被后画的列表遮挡。
            if (selectedTabIndex == BlocksTabIndex && compatibilityInfoButton.IsMouseHovering)
                UICommon.TooltipMouseText(ImproveGameCompatibilityText.Format(ImproveGameMultiplayerSystem.GetDisplayStatus(),
                    Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift)));
            else if (closeButton.IsMouseHovering)
                UICommon.TooltipMouseText(Language.GetTextValue("Mods.ComprehensiveStatistics.UI.CloseButtonTooltip"));
            else if (GetActiveStatisticsList()?.HoveredMetricTooltip is string metricTooltip)
                UICommon.TooltipMouseText(metricTooltip);
        }

        /// <summary>
        /// 当服务器同步世界统计增量时，刷新击杀页的世界实际死亡列。
        /// </summary>
        internal void RefreshWorldPublicData()
        {
            if (selectedTabIndex == KillsTabIndex && toolbar != null)
            {
                UpdateKillTotals(toolbar.Scope);
            }
        }

        /// <summary>
        /// 选择有效页面，并同步内容挂载和选中外观。
        /// </summary>
        private void SelectTab(int tabIndex, bool playSound = true, bool saveCurrent = true)
        {
            if (tabIndex < 0 || tabIndex >= TabCount)
            {
                return;
            }

            if (saveCurrent)
            {
                SaveActiveExpansionState();
                SaveActiveListViewState();
            }
            bool changed = selectedTabIndex != tabIndex;
            selectedTabIndex = tabIndex;
            UpdatePageHeading();
            SetBlockTotalsVisible(tabIndex == BlocksTabIndex);
            SetItemTotalsVisible(tabIndex == ItemsTabIndex);
            SetAmmoTotalsVisible(tabIndex == AmmoTabIndex);
            SetInteractionTotalsVisible(tabIndex == InteractionsTabIndex);
            SetKillTotalsVisible(tabIndex == KillsTabIndex);
            SetEconomyTotalsVisible(tabIndex == EconomyTabIndex);
            SetFishingTotalsVisible(tabIndex == FishingTabIndex && selectedFishingSubTab != "Bait");
            SetBaitTotalsVisible(tabIndex == FishingTabIndex && selectedFishingSubTab == "Bait");
            SetMovementTotalsVisible(tabIndex == MovementTabIndex);
            SetSurvivalTotalsVisible(tabIndex == SurvivalTabIndex);
            foreach (var pair in subTabBars)
            {
                pair.Value.Remove();
                if (pair.Key != TabLocalizationKeys[selectedTabIndex]) continue;
                contentPanel.Append(pair.Value);
                pair.Value.Select(pair.Key == "Fishing" ? selectedFishingSubTab : selectedBlockSubTab);
                pair.Value.Recalculate();
                pair.Value.RefreshLayout();
            }
            UpdateListLayout();

            // 列表是在切换 Tab 时动态挂载的，必须把工具栏重新放到绘制顺序末尾。
            // 否则 UISearchBar 画在输入框下方的 IME 候选面板会被列表行覆盖。
            toolbar.Remove();
            contentPanel.Append(toolbar);
            RestoreActiveExpansionState();
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
            float windowWidth = MathHelper.Min(
                PreferredWindowWidth,
                MathHelper.Max(460f, viewportWidth * WindowWidthViewportRatio));
            // 为统计列表提供更多纵向空间；仍保留画布边距并在小分辨率下响应式收缩。
            float windowHeight = MathHelper.Min(
                PreferredWindowHeight,
                MathHelper.Max(400f, viewportHeight * WindowHeightViewportRatio));
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
            // 高度不足时同步缩小间距，确保十个 Tab 仍全部留在窗口内。
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
            foreach (var bar in subTabBars.Values.Where(b => b.Parent != null)) bar.RefreshLayout();
            UpdateListLayout();
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
            RestoreActiveExpansionState();
        }

        /// <summary>
        /// 将公共筛选状态同步到所有可切换的统计列表。
        /// </summary>
        private void HandleFiltersChanged(
            string searchQuery,
            StatisticsContentSourceFilter sourceFilter)
        {
            foreach (VirtualizedStatisticsList statisticsList in statisticsLists.Values)
            {
                statisticsList.SetFilters(searchQuery, sourceFilter);
            }
            if (CanReadRuntimeStatistics)
                Main.LocalPlayer.GetModPlayer<StatisticsPlayer>().Data.ListViewStates.ResetScrollPositions();
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
        /// 取得当前页面的树形统计列表；无效索引返回空值。
        /// </summary>
        private VirtualizedStatisticsList GetActiveStatisticsList()
        {
            return statisticsLists != null && statisticsLists.TryGetValue(ActivePageKey, out var list) ? list : null;
        }

        private string ActivePageKey => selectedTabIndex == BlocksTabIndex
            ? "Blocks/" + selectedBlockSubTab : selectedTabIndex == FishingTabIndex
                ? "Fishing/" + selectedFishingSubTab : TabLocalizationKeys[selectedTabIndex];

        /// <summary>保存旧子页后切换独立列表，工具栏筛选和范围保持当前值。</summary>
        private void SelectSubTab(string mainTab, string id)
        {
            id = StatisticsSubTabs.Resolve(mainTab, id);
            if (id == (mainTab == "Fishing" ? selectedFishingSubTab : selectedBlockSubTab)) return;
            SaveActiveExpansionState();
            SaveActiveListViewState();
            GetActiveStatisticsList()?.Remove();
            if (mainTab == "Fishing")
            {
                selectedFishingSubTab = id;
                isFishingListAttached = false;
                isBaitListAttached = false;
            }
            else
            {
                selectedBlockSubTab = id;
                isBlockListAttached = false;
            }
            if (CanReadRuntimeStatistics)
                Main.LocalPlayer.GetModPlayer<StatisticsPlayer>().Data.SelectedSubTabs[mainTab] = id;
            SelectTab(selectedTabIndex, playSound: false, saveCurrent: false);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        /// <summary>统一排列筛选、固定子页栏与列表；无子页的主页面不留下空行。</summary>
        private void UpdateListLayout()
        {
            if (statisticsLists == null) return;
            foreach (var pair in statisticsLists)
            {
                string main = pair.Key.Split('/')[0];
                float top = ListTop;
                if (subTabBars.TryGetValue(main, out var bar)) top = bar.Top.Pixels + bar.Height.Pixels + 6f;
                pair.Value.Top.Set(top, 0f);
                pair.Value.Height.Set(-top, 1f);
            }
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 将用户刚刚提交的当前 Tab 折叠状态写入人物数据中的对应范围。
        /// </summary>
        private void SaveActiveExpansionState()
        {
            if (!CanReadRuntimeStatistics)
            {
                return;
            }

            VirtualizedStatisticsList activeList = GetActiveStatisticsList();
            if (activeList == null)
            {
                return;
            }

            StatisticsViewScope scope = toolbar?.Scope ?? StatisticsViewScope.CharacterCareer;
            Main.LocalPlayer.GetModPlayer<StatisticsPlayer>().SaveExpansionStates(
                ActivePageKey,
                currentWorld: scope == StatisticsViewScope.CharacterCurrentWorld,
                activeList.GetExpansionStates());
        }

        /// <summary>立即保存当前 Tab 的排序与滚动快照，无需等待窗口关闭。</summary>
        private void SaveActiveListViewState()
        {
            if (!CanReadRuntimeStatistics) return;
            VirtualizedStatisticsList list = GetActiveStatisticsList();
            if (list == null) return;
            Main.LocalPlayer.GetModPlayer<StatisticsPlayer>().SaveListViewState(
                ActivePageKey,
                toolbar.Scope == StatisticsViewScope.CharacterCurrentWorld, list.GetViewState());
        }

        /// <summary>
        /// 从人物存档恢复当前 Tab 在人物生涯或当前世界中的折叠、排序和滚动状态。
        /// </summary>
        private void RestoreActiveExpansionState()
        {
            if (!CanReadRuntimeStatistics)
            {
                return;
            }

            VirtualizedStatisticsList activeList = GetActiveStatisticsList();
            if (activeList == null)
            {
                return;
            }

            StatisticsViewScope scope = toolbar?.Scope ?? StatisticsViewScope.CharacterCareer;
            IReadOnlyDictionary<string, bool> savedStates = Main.LocalPlayer
                .GetModPlayer<StatisticsPlayer>()
                .GetSavedExpansionStates(
                    ActivePageKey,
                    currentWorld: scope == StatisticsViewScope.CharacterCurrentWorld);
            activeList.RestoreExpansionStates(savedStates);
            activeList.RestoreViewState(Main.LocalPlayer.GetModPlayer<StatisticsPlayer>()
                .GetSavedListViewState(ActivePageKey,
                    scope == StatisticsViewScope.CharacterCurrentWorld));
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
            float headerButtonsWidth = ExpandCollapseButtonWidth + 12f;
            if (selectedTabIndex == BlocksTabIndex)
            {
                headerButtonsWidth += CompatibilityButtonWidth + CompatibilityButtonGap;
                if (compatibilityInfoButton.Parent == null) contentPanel.Append(compatibilityInfoButton);
            }
            else if (compatibilityInfoButton.Parent != null) compatibilityInfoButton.Remove();

            contentDescription.Left.Set(descriptionLeft, 0f);
            contentDescription.Width.Set(
                -descriptionLeft - headerButtonsWidth,
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

            if (CanReadRuntimeStatistics)
            {
                startedAtUtc = GetCharacterScopeData(scope)?.StartedAtUtc;
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
            UpdateBlockTotals(scope);
            UpdateItemTotals(scope);
            UpdateAmmoTotals(scope);
            UpdateInteractionTotals(scope);
            UpdateKillTotals(scope);
            UpdateEconomyTotals(scope);
            UpdateFishingTotals(scope);
            UpdateBaitTotals(scope);
        }

        /// <summary>
        /// 合并当前角色范围的击杀数据与当前世界实际死亡，生成三列 NPC 明细。
        /// </summary>
        private void UpdateKillTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != KillsTabIndex)
            {
                return;
            }

            StatisticsData characterData = GetCharacterScopeData(scope);
            StatisticsData worldData = ModContent.GetInstance<StatisticsWorldSystem>().Data;
            killList.SetGroups(CreateKillGroups(characterData, worldData));
        }

        /// <summary>
        /// 从当前角色范围重新生成 NPC 对话与设施交互分类和明细。
        /// </summary>
        private void UpdateInteractionTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != InteractionsTabIndex)
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            interactionList.SetGroups(CreateInteractionGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成物块、背景墙与布线组件的放置和拆除明细。
        /// </summary>
        private void UpdateBlockTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != BlocksTabIndex)
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            blockList.SetGroups(CreateBlockPageGroups(scopeData, selectedBlockSubTab));
        }

        /// <summary>
        /// 从当前角色范围重新生成物品页的拾取、使用与丢弃统计。
        /// </summary>
        private void UpdateItemTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != ItemsTabIndex)
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            itemList.SetGroups(CreateItemGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成弹药应耗与实际消耗统计。
        /// </summary>
        private void UpdateAmmoTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != AmmoTabIndex)
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            ammoList.SetGroups(CreateAmmoGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成经济页的买卖明细与护士治疗统计。
        /// </summary>
        private void UpdateEconomyTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != EconomyTabIndex)
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            economyList.SetGroups(CreateEconomyGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成当前渔获或按鱼竿分组的抛竿子页。
        /// </summary>
        private void UpdateFishingTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != FishingTabIndex || selectedFishingSubTab == "Bait")
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            fishingList.SetGroups(selectedFishingSubTab == "Casts"
                ? FishingCastStatisticsList.CreateGroups(scopeData) : CreateFishingGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成鱼饵应耗、实耗与消耗率明细。
        /// </summary>
        private void UpdateBaitTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || (selectedTabIndex != FishingTabIndex || selectedFishingSubTab != "Bait"))
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            baitList.SetGroups(CreateBaitGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成移动页的总距离统计。
        /// </summary>
        private void UpdateMovementTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != MovementTabIndex)
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            movementList.SetGroups(CreateMovementGroups(scopeData));
        }

        /// <summary>
        /// 从当前角色范围重新生成生存页的统计分组。
        /// </summary>
        private void UpdateSurvivalTotals(StatisticsViewScope scope)
        {
            if (!CanReadRuntimeStatistics || selectedTabIndex != SurvivalTabIndex)
            {
                return;
            }

            StatisticsData scopeData = GetCharacterScopeData(scope);
            survivalList.SetGroups(CreateSurvivalGroups(scopeData));
        }

        /// <summary>
        /// 将工具栏角色范围统一解析为对应统计数据；世界公共数据不经过此入口。
        /// </summary>
        private static StatisticsData GetCharacterScopeData(StatisticsViewScope scope)
        {
            StatisticsPlayer statisticsPlayer =
                Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            return scope switch
            {
                StatisticsViewScope.CharacterCareer => statisticsPlayer.Data?.Career,
                StatisticsViewScope.CharacterCurrentWorld => statisticsPlayer.CurrentWorldSlice,
                _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
            };
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
        /// 按需挂载弹药统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetAmmoTotalsVisible(bool visible)
        {
            if (visible == isAmmoListAttached)
            {
                if (visible)
                {
                    UpdateAmmoTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(ammoList);
                UpdateAmmoTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                ammoList.Remove();
            }

            isAmmoListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 按需挂载实体物块统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetBlockTotalsVisible(bool visible)
        {
            if (visible == isBlockListAttached)
            {
                if (visible)
                {
                    UpdateBlockTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(blockList);
                UpdateBlockTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                blockList.Remove();
            }

            isBlockListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 按需挂载交互统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetInteractionTotalsVisible(bool visible)
        {
            if (visible == isInteractionListAttached)
            {
                if (visible)
                {
                    UpdateInteractionTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(interactionList);
                UpdateInteractionTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                interactionList.Remove();
            }

            isInteractionListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 将 NPC 对话和设施统计转换为交互页的五个一级分组。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateInteractionGroups(StatisticsData data)
        {
            List<StatisticsListItem> buffStations = new List<StatisticsListItem>();
            List<StatisticsListItem> furniture = new List<StatisticsListItem>();
            List<StatisticsListItem> containers = new List<StatisticsListItem>();
            List<StatisticsListItem> utilities = new List<StatisticsListItem>();

            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (pair.Value.Events <= 0L
                        || !pair.Key.Content.HasValue
                        || !StatisticKeys.TryParseFacilityCategory(
                            pair.Key.StatisticId,
                            out FacilityCategory category))
                    {
                        continue;
                    }

                    StatisticsListItem item = CreateFacilityStatisticsItem(
                        pair.Key.Content.Value,
                        pair.Key.Style,
                        pair.Value.Events);
                    switch (category)
                    {
                        case FacilityCategory.BuffStation:
                            buffStations.Add(item);
                            break;
                        case FacilityCategory.Furniture:
                            furniture.Add(item);
                            break;
                        case FacilityCategory.Container:
                            containers.Add(item);
                            break;
                        case FacilityCategory.Pylon:
                        case FacilityCategory.Clock:
                        case FacilityCategory.Mechanism:
                            utilities.Add(item);
                            break;
                    }
                }
            }

            return new[]
            {
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Interactions.DialogueGroup",
                    Array.Empty<StatisticsListItem>(),
                    nameIsLocalizationKey: true,
                    childGroups: CreateNpcDialogueGroups(data),
                    metricDisplay: StatisticsMetricDisplay.Events),
                CreateInteractionGroup("BuffStationsGroup", buffStations),
                CreateInteractionGroup("FurnitureGroup", furniture),
                CreateInteractionGroup("ContainersGroup", containers),
                CreateInteractionGroup("UtilitiesGroup", utilities)
            };
        }

        private static StatisticsListGroup CreateInteractionGroup(
            string localizationName,
            IEnumerable<StatisticsListItem> items)
        {
            return new StatisticsListGroup(
                $"Mods.ComprehensiveStatistics.UI.Interactions.{localizationName}",
                items,
                nameIsLocalizationKey: true,
                metricDisplay: StatisticsMetricDisplay.Events);
        }

        /// <summary>
        /// 使用注册定义解析设施名称和放置物品图标；定义缺失时仍保留存档中的稳定键。
        /// </summary>
        private static StatisticsListItem CreateFacilityStatisticsItem(
            StableContentKey content,
            string style,
            long events)
        {
            bool definitionResolved = FacilityRegistry.TryGetDefinition(
                content,
                style,
                out FacilityDefinition definition);
            int itemType = definitionResolved ? definition.IconItemType : 0;
            string name = definitionResolved
                ? Lang.GetItemNameValue(itemType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Interactions.MissingFacilityContent",
                    $"{content}#{style}");
            Asset<Texture2D> icon = null;
            if (definitionResolved)
            {
                Main.instance.LoadItem(itemType);
                icon = TextureAssets.Item[itemType];
            }

            StatisticsContentSource source = GetContentSource(content);
            int? vanillaTileId = content.TryResolveTileType(out int tileType)
                && source == StatisticsContentSource.Vanilla
                    ? tileType
                    : null;
            return new StatisticsListItem(
                name,
                itemType,
                icon,
                events,
                units: 0L,
                source: source,
                iconKind: definitionResolved ? StatisticsIconKind.Item : StatisticsIconKind.None,
                content: content,
                vanillaContentId: vanillaTileId);
        }

        /// <summary>
        /// 按需挂载击杀统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetKillTotalsVisible(bool visible)
        {
            if (visible == isKillListAttached)
            {
                if (visible)
                {
                    UpdateKillTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(killList);
                UpdateKillTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                killList.Remove();
            }

            isKillListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 按 NPC 合并角色最终击杀、协助击杀与当前世界实际死亡三列数据。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateKillGroups(
            StatisticsData characterData,
            StatisticsData worldData)
        {
            Dictionary<StableContentKey, long> finalKillsByNpc =
                new Dictionary<StableContentKey, long>();
            Dictionary<StableContentKey, long> assistsByNpc =
                new Dictionary<StableContentKey, long>();
            Dictionary<StableContentKey, long> worldDeathsByNpc =
                new Dictionary<StableContentKey, long>();
            if (characterData != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in characterData.Entries)
                {
                    if (!pair.Key.Content.HasValue || pair.Value.Events <= 0L)
                    {
                        continue;
                    }

                    StableContentKey content = pair.Key.Content.Value;
                    if (string.Equals(
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
                }
            }

            if (worldData != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in worldData.Entries)
                {
                    if (!pair.Key.Content.HasValue
                        || pair.Value.Events <= 0L
                        || !string.Equals(
                            pair.Key.StatisticId,
                            StatisticKeys.NpcWorldDeathStatisticId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    StableContentKey content = pair.Key.Content.Value;
                    worldDeathsByNpc.TryGetValue(content, out long current);
                    worldDeathsByNpc[content] = SaturatingAdd(current, pair.Value.Events);
                }
            }

            HashSet<StableContentKey> npcContents =
                new HashSet<StableContentKey>(finalKillsByNpc.Keys);
            npcContents.UnionWith(assistsByNpc.Keys);
            npcContents.UnionWith(worldDeathsByNpc.Keys);
            List<StableContentKey> orderedNpcContents =
                new List<StableContentKey>(npcContents);
            orderedNpcContents.Sort(CompareNpcContentKeys);

            List<StatisticsListItem> items =
                new List<StatisticsListItem>(orderedNpcContents.Count);
            foreach (StableContentKey content in orderedNpcContents)
            {
                finalKillsByNpc.TryGetValue(content, out long finalKills);
                assistsByNpc.TryGetValue(content, out long assists);
                worldDeathsByNpc.TryGetValue(content, out long worldDeaths);
                items.Add(CreateNpcStatisticsItem(
                    content,
                    finalKills,
                    units: assists,
                    amount: worldDeaths));
            }

            return new[]
            {
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Kills.CombinedGroup",
                    items,
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.KillOutcomes)
            };
        }

        /// <summary>
        /// 将开始对话与对话按钮按 NPC 汇总为交互页的二级分组。
        /// </summary>
        private static IReadOnlyList<StatisticsListGroup> CreateNpcDialogueGroups(
            StatisticsData data)
        {
            Dictionary<StableContentKey, long> conversationsByNpc =
                new Dictionary<StableContentKey, long>();
            Dictionary<StableContentKey, Dictionary<NpcChatButtonKind, long>> buttonsByNpc =
                new Dictionary<StableContentKey, Dictionary<NpcChatButtonKind, long>>();

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
                        conversationsByNpc[content] = SaturatingAdd(current, pair.Value.Events);
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
                }
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

            return npcGroups;
        }

        /// <summary>
        /// 将按 NPC 汇总的事件字典转换为默认内容顺序的统计叶子项。
        /// </summary>
        private static List<StatisticsListItem> CreateNpcStatisticsItems(
            IReadOnlyDictionary<StableContentKey, long> eventsByNpc,
            bool mirrorEventsToUnits = false)
        {
            List<StableContentKey> orderedContents =
                new List<StableContentKey>(eventsByNpc.Keys);
            orderedContents.Sort(CompareNpcContentKeys);
            List<StatisticsListItem> items =
                new List<StatisticsListItem>(orderedContents.Count);
            foreach (StableContentKey content in orderedContents)
            {
                items.Add(CreateNpcStatisticsItem(
                    content,
                    eventsByNpc[content],
                    units: mirrorEventsToUnits ? eventsByNpc[content] : 0L));
            }

            return items;
        }

        /// <summary>
        /// 将 NPC 稳定内容键和最多三项指标转换为带 NPC 首帧图标的统计叶子项。
        /// </summary>
        private static StatisticsListItem CreateNpcStatisticsItem(
            StableContentKey content,
            long events,
            long units = 0L,
            long amount = 0L,
            long? rateUnits = null,
            long? rateAmount = null)
        {
            bool resolved = content.TryResolveNpcType(out int npcType);
            StatisticsContentSource source = GetContentSource(content);
            string name = resolved
                ? Lang.GetNPCNameValue(npcType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Kills.MissingNpcContent",
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
                units: units,
                amount: amount,
                source: source,
                iconKind: resolved ? StatisticsIconKind.Npc : StatisticsIconKind.None,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? npcType
                    : null,
                rateUnits: rateUnits,
                rateAmount: rateAmount);
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
                    "Mods.ComprehensiveStatistics.UI.Interactions.MissingNpcContent",
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
                    "Mods.ComprehensiveStatistics.UI.Interactions.ConversationStarted",
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
                    $"Mods.ComprehensiveStatistics.UI.Interactions.ChatButtons.{buttonKind}",
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
                iconKind: resolved ? StatisticsIconKind.Npc : StatisticsIconKind.None,
                stateKey: $"npc:{content}");
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
                        amountIcon: currencyIcon,
                        stateKey: $"merchant:{merchant.Key}"));
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
                    amountIcon: currencyIcon,
                    stateKey: $"currency:{currency.Key}"));
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
                    iconKind: StatisticsIconKind.Npc,
                    stateKey: $"merchant:{merchant.Key}"));
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

        /// <summary>按稳定子页身份建立互相隔离的分组，沿用旧分组路径。</summary>
        private static IEnumerable<StatisticsListGroup> CreateBlockPageGroups(StatisticsData data, string subTab)
        {
            if (subTab == "Liquids") return CreateLiquidGroups(data);
            string root = StatisticsSubTabs.For("Blocks").First(p => p.Id == subTab).GroupKey;
            return CreateBlockGroups(data).Where(group => group.StateKey == root);
        }

        /// <summary>将四种液体的原始体积投影到双列列表，不在展示时修改累计。</summary>
        private static IEnumerable<StatisticsListGroup> CreateLiquidGroups(StatisticsData data)
        {
            return LiquidStatisticsList.CreateGroups(data, iconType =>
            {
                Main.instance?.LoadItem(iconType);
                return TextureAssets.Item[iconType];
            });
        }

        /// <summary>
        /// 将物块、背景墙和布线组件记录转换为三个一级分组。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateBlockGroups(StatisticsData data)
        {
            List<StatisticsListItem> tiles = new List<StatisticsListItem>();
            List<StatisticsListItem> walls = new List<StatisticsListItem>();
            List<StatisticsListItem> wiringComponents = new List<StatisticsListItem>();
            Dictionary<StableContentKey, StatisticMetrics> normalizedTiles =
                new Dictionary<StableContentKey, StatisticMetrics>();
            Dictionary<StableContentKey, StatisticMetrics> normalizedWalls =
                new Dictionary<StableContentKey, StatisticMetrics>();
            Dictionary<StatisticKey, StatisticMetrics> normalizedTileObjects =
                new Dictionary<StatisticKey, StatisticMetrics>();
            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (!pair.Key.Content.HasValue
                        || pair.Value.Events <= 0L)
                    {
                        continue;
                    }

                    if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.TilePlacedStatisticId,
                        StringComparison.Ordinal))
                    {
                        AddActivityMetrics(
                            normalizedTiles,
                            pair.Key.Content.Value,
                            placed: pair.Value.Events,
                            removed: 0L);
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.TileMinedStatisticId,
                        StringComparison.Ordinal))
                    {
                        AddActivityMetrics(
                            normalizedTiles,
                            pair.Key.Content.Value,
                            placed: 0L,
                            removed: pair.Value.Events);
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.WallPlacedStatisticId,
                        StringComparison.Ordinal))
                    {
                        AddActivityMetrics(
                            normalizedWalls,
                            pair.Key.Content.Value,
                            placed: pair.Value.Events,
                            removed: 0L);
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.WallRemovedStatisticId,
                        StringComparison.Ordinal))
                    {
                        AddActivityMetrics(
                            normalizedWalls,
                            pair.Key.Content.Value,
                            placed: 0L,
                            removed: pair.Value.Events);
                    }
                    else if (string.Equals(
                            pair.Key.StatisticId,
                            StatisticKeys.TileObjectPlacedStatisticId,
                            StringComparison.Ordinal)
                        || string.Equals(
                            pair.Key.StatisticId,
                            StatisticKeys.TileObjectRemovedStatisticId,
                            StringComparison.Ordinal))
                    {
                        StableContentKey content = pair.Key.Content.Value;
                        string style = NormalizePersistedTileObjectStyle(
                            content,
                            pair.Key.Style);
                        StatisticKey normalizedKey = StatisticKeys.GetRemovedTileObjectKey(
                            content,
                            style);
                        normalizedTileObjects.TryGetValue(
                            normalizedKey,
                            out StatisticMetrics current);
                        bool placed = string.Equals(
                            pair.Key.StatisticId,
                            StatisticKeys.TileObjectPlacedStatisticId,
                            StringComparison.Ordinal);
                        normalizedTileObjects[normalizedKey] = current.Add(
                            new StatisticMetrics(
                                events: placed ? pair.Value.Events : 0L,
                                units: placed ? 0L : pair.Value.Events));
                    }
                }
            }

            foreach (KeyValuePair<StableContentKey, StatisticMetrics> pair in normalizedTiles)
            {
                tiles.Add(CreateTileStatisticsItem(
                    pair.Key,
                    pair.Value.Events,
                    pair.Value.Units));
            }

            foreach (KeyValuePair<StableContentKey, StatisticMetrics> pair in normalizedWalls)
            {
                walls.Add(CreateWallStatisticsItem(
                    pair.Key,
                    pair.Value.Events,
                    pair.Value.Units));
            }

            foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in normalizedTileObjects)
            {
                // 家具和多格物体保留独立采集口径与样式键，但与普通物块显示在同一一级分组。
                tiles.Add(CreateTileObjectStatisticsItem(
                    pair.Key.Content.Value,
                    pair.Key.Style,
                    pair.Value.Events,
                    pair.Value.Units));
            }

            foreach (WiringComponentKind component in Enum.GetValues<WiringComponentKind>())
            {
                StatisticMetrics metrics = data?.GetValue(
                    StatisticKeys.GetWiringComponentActivityKey(component)) ?? default;
                if (metrics.IsZero)
                {
                    continue;
                }

                StableContentKey content = StatisticKeys.GetWiringComponentContent(component);
                Asset<Texture2D> icon = GetWiringComponentIcon(component);
                wiringComponents.Add(new StatisticsListItem(
                    $"Mods.ComprehensiveStatistics.UI.Blocks.WiringComponents.{component}",
                    itemType: 0,
                    icon: icon,
                    events: metrics.Events,
                    units: metrics.Units,
                    source: StatisticsContentSource.Vanilla,
                    nameIsLocalizationKey: true,
                    iconKind: StatisticsIconKind.Tile,
                    content: content,
                    vanillaContentId: (int)component));
            }

            return new[]
            {
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Blocks.SolidTilesGroup",
                    tiles,
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.WiringActivity),
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Blocks.BackgroundWallsGroup",
                    walls,
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.WiringActivity),
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Blocks.WiringComponentsGroup",
                    wiringComponents,
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.WiringActivity)
            };
        }

        /// <summary>
        /// 获取四种电线和制动器各自的原版世界组件贴图，用作统计列表图标。
        /// </summary>
        private static Asset<Texture2D> GetWiringComponentIcon(WiringComponentKind component)
        {
            return component switch
            {
                WiringComponentKind.RedWire => TextureAssets.Wire,
                WiringComponentKind.BlueWire => TextureAssets.Wire2,
                WiringComponentKind.GreenWire => TextureAssets.Wire3,
                WiringComponentKind.YellowWire => TextureAssets.Wire4,
                WiringComponentKind.Actuator => TextureAssets.Actuator,
                _ => null
            };
        }

        /// <summary>
        /// 将一项放置或拆除计数累加到界面共用的双列指标中。
        /// </summary>
        private static void AddActivityMetrics<TKey>(
            Dictionary<TKey, StatisticMetrics> target,
            TKey key,
            long placed,
            long removed)
        {
            target.TryGetValue(key, out StatisticMetrics current);
            target[key] = current.Add(new StatisticMetrics(placed, removed));
        }

        /// <summary>
        /// 将旧存档中误识别为样式的开关状态帧归一化，使其合并回正确的物品条目。
        /// </summary>
        private static string NormalizePersistedTileObjectStyle(
            StableContentKey content,
            string persistedStyle)
        {
            if (!int.TryParse(
                    persistedStyle,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int style)
                || style < 0
                || !content.TryResolveTileType(out int tileType))
            {
                return persistedStyle;
            }

            return FacilityRegistry.NormalizeTileObjectStyle(tileType, style)
                .ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 按稳定物块键和样式恢复家具或多格物体名称，并优先显示对应放置/掉落物品图标。
        /// </summary>
        private static StatisticsListItem CreateTileObjectStatisticsItem(
            StableContentKey content,
            string persistedStyle,
            long placedCount,
            long removedCount)
        {
            bool resolved = content.TryResolveTileType(out int tileType);
            bool styleResolved = int.TryParse(
                persistedStyle,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int style)
                && style >= 0;
            StatisticsContentSource source = GetContentSource(content);
            int iconItemType = ItemID.None;
            if (resolved && styleResolved)
            {
                iconItemType = FindPlacementItemForTile(tileType, style);
                if (iconItemType <= ItemID.None)
                {
                    iconItemType = FindRegisteredModTileDrop(tileType, style);
                }
            }

            string name;
            if (iconItemType > ItemID.None)
            {
                name = Lang.GetItemNameValue(iconItemType);
            }
            else if (resolved && styleResolved)
            {
                string baseName = GetLocalizedTileName(content, tileType, out _);
                name = Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Blocks.TileObjectStyleFormat",
                    baseName,
                    style);
            }
            else
            {
                name = Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Blocks.MissingTileObjectContent",
                    $"{content}#{persistedStyle}");
            }

            Asset<Texture2D> icon = null;
            StatisticsTileIconLayout tileIconLayout = null;
            StatisticsIconKind iconKind = StatisticsIconKind.None;
            int iconType = 0;
            if (iconItemType > ItemID.None)
            {
                Main.instance.LoadItem(iconItemType);
                icon = TextureAssets.Item[iconItemType];
                iconKind = StatisticsIconKind.Item;
                iconType = iconItemType;
            }
            else if (resolved)
            {
                Main.instance.LoadTiles(tileType);
                icon = TextureAssets.Tile[tileType];
                iconKind = StatisticsIconKind.Tile;
                iconType = tileType;
                tileIconLayout = GetTileObjectIconLayout(tileType, style);
            }

            return new StatisticsListItem(
                name,
                itemType: iconType,
                icon: icon,
                events: placedCount,
                units: removedCount,
                source: source,
                iconKind: iconKind,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? tileType
                    : null,
                tileIconLayout: tileIconLayout);
        }

        /// <summary>
        /// 计算物块图集中指定样式的完整对象区域；例如罐子应显示整张 2×2 图案而非左上角一格。
        /// </summary>
        private static StatisticsTileIconLayout GetTileObjectIconLayout(
            int tileType,
            int style)
        {
            if (tileType == TileID.Pots || tileType == TileID.PotsEcho)
            {
                // 原版罐子由特殊逻辑管理且没有对应放置物品；固定使用图集中的完整首个 2×2 罐子。
                return new StatisticsTileIconLayout(
                    sourceX: 0,
                    sourceY: 0,
                    columns: 2,
                    coordinateWidth: 16,
                    coordinatePadding: 2,
                    coordinateHeights: new[] { 16, 16 });
            }

            TileObjectData data = TileObjectData.GetTileData(tileType, style);
            if (data == null
                || data.Width <= 0
                || data.Height <= 0
                || data.CoordinateWidth <= 0
                || data.CoordinateHeights == null
                || data.CoordinateHeights.Length < data.Height
                || data.CoordinateFullWidth <= 0
                || data.CoordinateFullHeight <= 0)
            {
                return null;
            }

            int placementStyle = data.CalculatePlacementStyle(style, alternate: 0, random: 0);
            int styleOffset = Math.Max(placementStyle, 0);
            int column = data.StyleHorizontal ? styleOffset : 0;
            int row = data.StyleHorizontal ? 0 : styleOffset;
            if (data.StyleWrapLimit > 0)
            {
                if (data.StyleHorizontal)
                {
                    column = styleOffset % data.StyleWrapLimit;
                    row = styleOffset / data.StyleWrapLimit;
                }
                else
                {
                    row = styleOffset % data.StyleWrapLimit;
                    column = styleOffset / data.StyleWrapLimit;
                }
            }

            int lineSkip = Math.Max(data.StyleLineSkip, 1);
            int x = column * data.CoordinateFullWidth
                * (data.StyleHorizontal ? 1 : lineSkip);
            int y = row * data.CoordinateFullHeight
                * (data.StyleHorizontal ? lineSkip : 1);
            int[] coordinateHeights = new int[data.Height];
            Array.Copy(data.CoordinateHeights, coordinateHeights, data.Height);
            return new StatisticsTileIconLayout(
                x,
                y,
                data.Width,
                data.CoordinateWidth,
                data.CoordinatePadding,
                coordinateHeights);
        }

        /// <summary>
        /// 将稳定物块键解析为本地化名称，并优先使用对应物品图标；缺失内容仍保留可读占位。
        /// </summary>
        private static StatisticsListItem CreateTileStatisticsItem(
            StableContentKey content,
            long placedCount,
            long removedCount)
        {
            bool resolved = content.TryResolveTileType(out int tileType);
            StatisticsContentSource source = GetContentSource(content);
            int iconItemType = ItemID.None;
            string name = resolved
                ? GetLocalizedTileName(content, tileType, out iconItemType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Blocks.MissingTileContent",
                    content.ToString());

            Asset<Texture2D> icon = null;
            StatisticsTileIconLayout tileIconLayout = null;
            StatisticsIconKind iconKind = StatisticsIconKind.None;
            int iconType = 0;
            if (iconItemType > ItemID.None)
            {
                Main.instance.LoadItem(iconItemType);
                icon = TextureAssets.Item[iconItemType];
                iconKind = StatisticsIconKind.Item;
                iconType = iconItemType;
            }
            else if (resolved)
            {
                Main.instance.LoadTiles(tileType);
                icon = TextureAssets.Tile[tileType];
                iconKind = StatisticsIconKind.Tile;
                iconType = tileType;
                tileIconLayout = GetTileObjectIconLayout(tileType, style: 0);
            }

            return new StatisticsListItem(
                name,
                itemType: iconType,
                icon: icon,
                events: placedCount,
                units: removedCount,
                source: source,
                iconKind: iconKind,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? tileType
                    : null,
                tileIconLayout: tileIconLayout);
        }

        /// <summary>
        /// 获取物块的当前语言名称，避免原版地图图例缺少翻译时直接显示内部英文名。
        /// </summary>
        private static string GetLocalizedTileName(
            StableContentKey content,
            int tileType,
            out int iconItemType)
        {
            iconItemType = FindPlacementItemForTile(tileType, style: 0);
            if (iconItemType <= ItemID.None)
            {
                iconItemType = FindRegisteredModTileDrop(tileType, style: 0);
            }

            string explicitMapKey = $"MapObject.{content.ContentName}";
            if (Language.Exists(explicitMapKey))
            {
                string explicitMapName = Language.GetTextValue(explicitMapKey);
                if (!string.IsNullOrWhiteSpace(explicitMapName)
                    && !string.Equals(
                        explicitMapName,
                        content.ContentName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return explicitMapName;
                }
            }

            string mapName = Lang.GetMapObjectName(MapHelper.TileToLookup(tileType, 0));
            if (!string.IsNullOrWhiteSpace(mapName)
                && !string.Equals(
                    mapName,
                    content.ContentName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return mapName;
            }

            string naturalTileLocalizationKey = GetNaturalTileLocalizationKey(content);
            if (naturalTileLocalizationKey != null)
            {
                return Language.GetTextValue(naturalTileLocalizationKey);
            }

            if (iconItemType > ItemID.None)
            {
                string itemName = Lang.GetItemNameValue(iconItemType);
                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    return itemName;
                }
            }

            return !string.IsNullOrWhiteSpace(mapName)
                ? mapName
                : content.ContentName;
        }

        /// <summary>
        /// 为原版地图图例仍返回内部名、且没有对应放置物品的自然装饰物提供本地化兜底。
        /// </summary>
        private static string GetNaturalTileLocalizationKey(StableContentKey content)
        {
            if (!string.Equals(content.ModName, "Terraria", StringComparison.Ordinal))
            {
                return null;
            }

            string key = content.ContentName switch
            {
                "Cattail" => "Cattail",
                "CorruptPlants" => "CorruptPlants",
                "MushroomPlants" => "MushroomPlants",
                "Plants2" => "Plants2",
                "SmallPiles" => "SmallPiles",
                "Stalactite" => "Stalactite",
                _ => null
            };
            return key == null
                ? null
                : $"Mods.ComprehensiveStatistics.UI.Blocks.NaturalTiles.{key}";
        }

        /// <summary>
        /// 查找能以指定样式放置物块的物品，并以最小类型 ID 保证结果稳定。
        /// </summary>
        private static int FindPlacementItemForTile(int tileType, int style)
        {
            int result = ItemID.None;
            foreach (KeyValuePair<int, Item> pair in ContentSamples.ItemsByType)
            {
                Item item = pair.Value;
                if (item == null
                    || item.IsAir
                    || item.createTile != tileType
                    || item.placeStyle != style
                    || (result > ItemID.None && pair.Key >= result))
                {
                    continue;
                }

                result = pair.Key;
            }

            return result;
        }

        /// <summary>
        /// 只对模组物块使用注册掉落作为图标；该 API 对原版物块不可靠，数字 ID 可能错误碰撞。
        /// </summary>
        private static int FindRegisteredModTileDrop(int tileType, int style)
        {
            return TileLoader.GetTile(tileType) != null
                ? TileLoader.GetItemDropFromTypeAndStyle(tileType, style)
                : ItemID.None;
        }

        /// <summary>
        /// 将稳定背景墙键解析为本地化物品名称和墙体贴图。
        /// </summary>
        private static StatisticsListItem CreateWallStatisticsItem(
            StableContentKey content,
            long placedCount,
            long removedCount)
        {
            bool resolved = content.TryResolveWallType(out int wallType);
            StatisticsContentSource source = GetContentSource(content);
            int iconItemType = ItemID.None;
            string name = resolved
                ? GetLocalizedWallName(content, wallType, out iconItemType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Blocks.MissingWallContent",
                    content.ToString());

            Asset<Texture2D> icon = null;
            StatisticsIconKind iconKind = StatisticsIconKind.None;
            int iconType = 0;
            if (iconItemType > ItemID.None)
            {
                Main.instance.LoadItem(iconItemType);
                icon = TextureAssets.Item[iconItemType];
                iconKind = StatisticsIconKind.Item;
                iconType = iconItemType;
            }
            else if (resolved)
            {
                Main.instance.LoadWall(wallType);
                icon = TextureAssets.Wall[wallType];
                iconKind = StatisticsIconKind.Wall;
                iconType = wallType;
            }

            return new StatisticsListItem(
                name,
                itemType: iconType,
                icon: icon,
                events: placedCount,
                units: removedCount,
                source: source,
                iconKind: iconKind,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? wallType
                    : null);
        }

        /// <summary>
        /// 查找能放置指定背景墙的物品，并以最小类型 ID 保证结果稳定。
        /// </summary>
        private static int FindPlacementItemForWall(int wallType)
        {
            int result = ItemID.None;
            foreach (KeyValuePair<int, Item> pair in ContentSamples.ItemsByType)
            {
                Item item = pair.Value;
                if (item == null
                    || item.IsAir
                    || item.createWall != wallType
                    || (result > ItemID.None && pair.Key >= result))
                {
                    continue;
                }

                result = pair.Key;
            }

            return result;
        }

        /// <summary>
        /// 获取背景墙的当前语言名称，并为天然墙选择辨识度更高的代表物品图标。
        /// </summary>
        private static string GetLocalizedWallName(
            StableContentKey content,
            int wallType,
            out int iconItemType)
        {
            iconItemType = FindPlacementItemForWall(wallType);
            if (iconItemType > ItemID.None)
            {
                return Lang.GetItemNameValue(iconItemType);
            }

            if (!string.Equals(content.ModName, "Terraria", StringComparison.Ordinal))
            {
                return content.ContentName;
            }

            string naturalKind = NormalizeNaturalWallName(
                content.ContentName,
                out int variant);
            if (TryFindRepresentativeWallItem(naturalKind, out iconItemType))
            {
                string representativeName = Lang.GetItemNameValue(iconItemType);
                return variant > 0
                    ? Language.GetTextValue(
                        "Mods.ComprehensiveStatistics.UI.Blocks.NaturalWallVariantFormat",
                        representativeName,
                        variant)
                    : representativeName;
            }

            string localizationKey = GetNaturalWallLocalizationKey(
                naturalKind,
                out string representativeWallName);
            if (TryFindRepresentativeWallItem(representativeWallName, out int representativeItem))
            {
                iconItemType = representativeItem;
            }

            string localizedName = localizationKey != null
                ? Language.GetTextValue(localizationKey)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Blocks.NaturalWallFallbackFormat",
                    wallType);
            return variant > 0
                ? Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Blocks.NaturalWallVariantFormat",
                    localizedName,
                    variant)
                : localizedName;
        }

        /// <summary>
        /// 去掉天然墙内部名中的 Unsafe/Echo 标记并提取两侧的数字变体。
        /// </summary>
        private static string NormalizeNaturalWallName(string contentName, out int variant)
        {
            variant = 0;
            string normalized = contentName ?? string.Empty;
            int markerIndex = normalized.IndexOf("Unsafe", StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                string suffix = normalized.Substring(markerIndex + "Unsafe".Length);
                normalized = normalized.Substring(0, markerIndex);
                TryParsePositiveNumber(suffix, out variant);
            }
            else
            {
                markerIndex = normalized.IndexOf("Echo", StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    normalized = normalized.Substring(0, markerIndex);
                }
            }

            int digitStart = normalized.Length;
            while (digitStart > 0 && char.IsDigit(normalized[digitStart - 1]))
            {
                digitStart--;
            }

            if (digitStart < normalized.Length)
            {
                if (variant == 0)
                {
                    TryParsePositiveNumber(normalized.Substring(digitStart), out variant);
                }

                normalized = normalized.Substring(0, digitStart);
            }

            if (string.Equals(normalized, "CaveWall", StringComparison.Ordinal))
            {
                normalized = "Cave";
            }
            else if (string.Equals(normalized, "MudWall", StringComparison.Ordinal))
            {
                normalized = "Mud";
            }

            return normalized;
        }

        /// <summary>
        /// 安全解析正整数变体编号；失败时保持零值。
        /// </summary>
        private static bool TryParsePositiveNumber(string value, out int number)
        {
            return int.TryParse(value, out number) && number > 0;
        }

        /// <summary>
        /// 尝试通过规范化后的安全墙类型找到可放置物品，作为名称和图标来源。
        /// </summary>
        private static bool TryFindRepresentativeWallItem(
            string wallContentName,
            out int itemType)
        {
            itemType = ItemID.None;
            return !string.IsNullOrEmpty(wallContentName)
                && WallID.Search.TryGetId(wallContentName, out int wallType)
                && (itemType = FindPlacementItemForWall(wallType)) > ItemID.None;
        }

        /// <summary>
        /// 将没有安全墙对应物品的原版天然墙归入本地化类别，并指定代表墙图标。
        /// </summary>
        private static string GetNaturalWallLocalizationKey(
            string naturalKind,
            out string representativeWallName)
        {
            string key = naturalKind switch
            {
                "Cave" => "Cavern",
                "Ebonstone" => "Ebonstone",
                "Mud" => "Mud",
                "SnowWall" => "Snow",
                "Amethyst" => "Amethyst",
                "Topaz" => "Topaz",
                "Sapphire" => "Sapphire",
                "Emerald" => "Emerald",
                "Ruby" => "Ruby",
                "Diamond" => "Diamond",
                "CorruptGrass" => "CorruptGrass",
                "HallowedGrass" => "HallowedGrass",
                "Ice" => "Ice",
                "ObsidianBack" => "Obsidian",
                "CrimsonGrass" => "CrimsonGrass",
                "Crimstone" => "Crimstone",
                "Corruption" => "Corruption",
                "Crimson" => "Crimson",
                "Hallow" => "Hallow",
                "Pearlstone" => "Hallow",
                "Lava" => "Lava",
                "Rocks" => "Rocks",
                _ => null
            };
            representativeWallName = naturalKind switch
            {
                "Cave" or "Rocks" => "Stone",
                "Ebonstone" or "CorruptGrass" or "Corruption" => "EbonstoneBrick",
                "Mud" => "Dirt",
                "SnowWall" => "SnowBrick",
                "Ice" => "IceBrick",
                "ObsidianBack" => "ObsidianBrick",
                "CrimsonGrass" or "Crimstone" or "Crimson" => "CrimstoneBrick",
                "HallowedGrass" or "Hallow" => "PearlstoneBrick",
                "Lava" => "HellstoneBrick",
                "Amethyst" => "AmethystGemspark",
                "Topaz" => "TopazGemspark",
                "Sapphire" => "SapphireGemspark",
                "Emerald" => "EmeraldGemspark",
                "Ruby" => "RubyGemspark",
                "Diamond" => "DiamondGemspark",
                "Pearlstone" => "PearlstoneBrick",
                _ => string.Empty
            };
            return key == null
                ? null
                : $"Mods.ComprehensiveStatistics.UI.Blocks.NaturalWalls.{key}";
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
        /// 将具体弹药的应耗和实耗数据按原版或模组弹药类别组成独立表格。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateAmmoGroups(StatisticsData data)
        {
            Dictionary<int, List<StatisticsListItem>> itemsByCategory =
                new Dictionary<int, List<StatisticsListItem>>();
            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (!pair.Key.Content.HasValue
                        || pair.Value.IsZero
                        || !string.Equals(
                            pair.Key.StatisticId,
                            StatisticKeys.AmmoUsageStatisticId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    StatisticsListItem item = CreateAmmoStatisticsItem(
                        pair.Key.Content.Value,
                        pair.Value.Events,
                        pair.Value.Units,
                        pair.Value.Amount,
                        out int ammoCategory);
                    ammoCategory = NormalizeAmmoCategory(ammoCategory);
                    if (!itemsByCategory.TryGetValue(
                        ammoCategory,
                        out List<StatisticsListItem> categoryItems))
                    {
                        categoryItems = new List<StatisticsListItem>();
                        itemsByCategory.Add(ammoCategory, categoryItems);
                    }

                    categoryItems.Add(item);
                }
            }

            List<KeyValuePair<int, StatisticsListGroup>> keyedGroups =
                new List<KeyValuePair<int, StatisticsListGroup>>(itemsByCategory.Count);
            foreach (KeyValuePair<int, List<StatisticsListItem>> pair in itemsByCategory)
            {
                string localizationKey = GetAmmoCategoryLocalizationKey(pair.Key);
                string name = localizationKey ?? GetCustomAmmoCategoryName(pair.Key);
                bool nameIsLocalizationKey = localizationKey != null;
                Asset<Texture2D> icon = GetAmmoCategoryIcon(pair.Key, out int iconType);
                keyedGroups.Add(new KeyValuePair<int, StatisticsListGroup>(
                    pair.Key,
                    new StatisticsListGroup(
                        name,
                        pair.Value,
                        nameIsLocalizationKey: nameIsLocalizationKey,
                        metricDisplay: StatisticsMetricDisplay.AmmoActivity,
                        iconType: iconType,
                        icon: icon,
                        iconKind: StatisticsIconKind.Item,
                        stateKey: $"ammo:{pair.Key.ToString(CultureInfo.InvariantCulture)}")));
            }

            keyedGroups.Sort((left, right) =>
            {
                int orderComparison = GetAmmoCategoryOrder(left.Key).CompareTo(
                    GetAmmoCategoryOrder(right.Key));
                return orderComparison != 0
                    ? orderComparison
                    : string.Compare(
                        left.Value.Name,
                        right.Value.Name,
                        StringComparison.CurrentCulture);
            });

            List<StatisticsListGroup> groups =
                new List<StatisticsListGroup>(keyedGroups.Count);
            foreach (KeyValuePair<int, StatisticsListGroup> pair in keyedGroups)
            {
                groups.Add(pair.Value);
            }

            return groups;
        }

        /// <summary>
        /// 将弹药稳定内容键转换为带物品图标的三指标叶子项。
        /// </summary>
        private static StatisticsListItem CreateAmmoStatisticsItem(
            StableContentKey content,
            long supplyEvents,
            long expectedConsumption,
            long actualConsumption,
            out int ammoCategory)
        {
            bool resolved = content.TryResolveItemType(out int itemType);
            StatisticsContentSource source = GetContentSource(content);
            string name = resolved
                ? Lang.GetItemNameValue(itemType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Items.MissingItemContent",
                    content.ToString());
            Asset<Texture2D> icon = null;
            ammoCategory = AmmoID.None;
            bool isNonConsumable = false;
            if (resolved)
            {
                Item sample = ContentSamples.ItemsByType[itemType];
                ammoCategory = sample?.ammo ?? AmmoID.None;
                isNonConsumable = sample != null && !sample.consumable;
                Main.instance.LoadItem(itemType);
                icon = TextureAssets.Item[itemType];
            }

            // K3 早期测试数据把非消耗型弹药次数保存在 Events。
            // 当前口径不再展示或新增该指标，仅在读取时将旧值合并到应耗，
            // 避免无尽箭袋和无尽火枪弹袋已有的测试记录丢失。
            long displayedExpectedConsumption = isNonConsumable
                ? SaturatingAdd(expectedConsumption, supplyEvents)
                : expectedConsumption;

            return new StatisticsListItem(
                name,
                itemType: resolved ? itemType : ItemID.None,
                icon: icon,
                events: 0L,
                units: displayedExpectedConsumption,
                amount: actualConsumption,
                source: source,
                iconKind: resolved ? StatisticsIconKind.Item : StatisticsIconKind.None,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? itemType
                    : null,
                rateUnits: isNonConsumable ? 0L : displayedExpectedConsumption,
                rateAmount: isNonConsumable ? 0L : actualConsumption);
        }

        /// <summary>
        /// 将无可读代表物品的异常弹药类别合并到“其他”，避免生成多个同名空分组。
        /// </summary>
        private static int NormalizeAmmoCategory(int ammoCategory)
        {
            if (GetAmmoCategoryLocalizationKey(ammoCategory) != null)
            {
                return ammoCategory;
            }

            return ammoCategory > AmmoID.None
                && ContentSamples.ItemsByType.TryGetValue(ammoCategory, out Item sample)
                && sample != null
                && !sample.IsAir
                    ? ammoCategory
                    : AmmoID.None;
        }

        /// <summary>
        /// 获取原版标准弹药类别的本地化键；模组自定义类别返回空值并使用代表物品命名。
        /// </summary>
        private static string GetAmmoCategoryLocalizationKey(int ammoCategory)
        {
            string name = null;
            if (ammoCategory == AmmoID.Arrow) name = "Arrow";
            else if (ammoCategory == AmmoID.Bullet) name = "Bullet";
            else if (ammoCategory == AmmoID.Rocket) name = "Rocket";
            else if (ammoCategory == AmmoID.Dart) name = "Dart";
            else if (ammoCategory == AmmoID.Solution) name = "Solution";
            else if (ammoCategory == AmmoID.Gel) name = "Gel";
            else if (ammoCategory == AmmoID.FallenStar) name = "FallenStar";
            else if (ammoCategory == AmmoID.Sand) name = "Sand";
            else if (ammoCategory == AmmoID.Coin) name = "Coin";
            else if (ammoCategory == AmmoID.Flare) name = "Flare";
            else if (ammoCategory == AmmoID.Snowball) name = "Snowball";
            else if (ammoCategory == AmmoID.Stake) name = "Stake";
            else if (ammoCategory == AmmoID.CandyCorn) name = "CandyCorn";
            else if (ammoCategory == AmmoID.JackOLantern) name = "JackOLantern";
            else if (ammoCategory == AmmoID.NailFriendly) name = "Nail";
            else if (ammoCategory == AmmoID.StyngerBolt) name = "StyngerBolt";
            else if (ammoCategory == AmmoID.None) name = "Other";
            return name == null
                ? null
                : $"Mods.ComprehensiveStatistics.UI.Items.AmmoCategories.{name}";
        }

        /// <summary>
        /// 使用模组自定义弹药类别的代表物品名称生成分组标题。
        /// </summary>
        private static string GetCustomAmmoCategoryName(int ammoCategory)
        {
            if (ammoCategory > AmmoID.None
                && ContentSamples.ItemsByType.TryGetValue(ammoCategory, out Item sample)
                && sample != null
                && !sample.IsAir)
            {
                return Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Items.CustomAmmoCategoryFormat",
                    sample.Name);
            }

            return Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.Items.AmmoCategories.Other");
        }

        /// <summary>
        /// 获取弹药类别代表物品的图标；无有效代表物品时返回空图标。
        /// </summary>
        private static Asset<Texture2D> GetAmmoCategoryIcon(
            int ammoCategory,
            out int itemType)
        {
            itemType = ItemID.None;
            if (ammoCategory <= AmmoID.None
                || !ContentSamples.ItemsByType.TryGetValue(ammoCategory, out Item sample)
                || sample == null
                || sample.IsAir)
            {
                return null;
            }

            itemType = ammoCategory;
            Main.instance.LoadItem(itemType);
            return TextureAssets.Item[itemType];
        }

        /// <summary>
        /// 返回原版弹药类别的固定展示顺序，模组类别随后按本地化名称排列。
        /// </summary>
        private static int GetAmmoCategoryOrder(int ammoCategory)
        {
            if (ammoCategory == AmmoID.Arrow) return 0;
            if (ammoCategory == AmmoID.Bullet) return 1;
            if (ammoCategory == AmmoID.Rocket) return 2;
            if (ammoCategory == AmmoID.Dart) return 3;
            if (ammoCategory == AmmoID.Solution) return 4;
            if (ammoCategory == AmmoID.Gel) return 5;
            if (ammoCategory == AmmoID.FallenStar) return 6;
            if (ammoCategory == AmmoID.Sand) return 7;
            if (ammoCategory == AmmoID.Coin) return 8;
            if (ammoCategory == AmmoID.Flare) return 9;
            if (ammoCategory == AmmoID.Snowball) return 10;
            if (ammoCategory == AmmoID.Stake) return 11;
            if (ammoCategory == AmmoID.CandyCorn) return 12;
            if (ammoCategory == AmmoID.JackOLantern) return 13;
            if (ammoCategory == AmmoID.NailFriendly) return 14;
            if (ammoCategory == AmmoID.StyngerBolt) return 15;
            return ammoCategory == AmmoID.None ? int.MaxValue : 1000;
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
        /// 按需挂载钓鱼统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetFishingTotalsVisible(bool visible)
        {
            if (visible == isFishingListAttached)
            {
                if (visible)
                {
                    UpdateFishingTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(fishingList);
                UpdateFishingTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                fishingList.Remove();
            }

            isFishingListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 按需挂载鱼饵统计列表，并在显示时刷新当前范围的数据。
        /// </summary>
        private void SetBaitTotalsVisible(bool visible)
        {
            if (visible == isBaitListAttached)
            {
                if (visible)
                {
                    UpdateBaitTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(baitList);
                UpdateBaitTotals(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                baitList.Remove();
            }

            isBaitListAttached = visible;
            contentPanel.Recalculate();
        }

        /// <summary>
        /// 从一个统计范围创建按来源液体分组的渔获树。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateFishingGroups(StatisticsData data)
        {
            var groups = new List<StatisticsListGroup>();

            foreach (FishingLiquidKind liquid in Enum.GetValues(typeof(FishingLiquidKind)))
            {
                int iconItemType = GetFishingLiquidIconItemType(liquid);
                Main.instance.LoadItem(iconItemType);
                groups.Add(new StatisticsListGroup(
                    $"Mods.ComprehensiveStatistics.UI.Fishing.Liquids.{liquid}",
                    CreateFishingCatchItems(data, liquid),
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.FishingCatchActivity,
                    iconType: iconItemType,
                    icon: TextureAssets.Item[iconItemType],
                    iconKind: StatisticsIconKind.Item));
            }

            return groups;
        }

        /// <summary>
        /// 创建一个来源液体下合并显示的物品与 NPC 渔获列表。
        /// </summary>
        private static List<StatisticsListItem> CreateFishingCatchItems(
            StatisticsData data,
            FishingLiquidKind liquid)
        {
            Dictionary<StableContentKey, StatisticMetrics> itemCatches =
                new Dictionary<StableContentKey, StatisticMetrics>();
            Dictionary<StableContentKey, long> npcCatches =
                new Dictionary<StableContentKey, long>();

            if (data != null)
            {
                foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
                {
                    if (!pair.Key.Content.HasValue
                        || !StatisticKeys.TryParseFishingLiquidKind(
                            pair.Key.Style,
                            out FishingLiquidKind sourceLiquid)
                        || sourceLiquid != liquid)
                    {
                        continue;
                    }

                    if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.FishingItemCatchStatisticId,
                        StringComparison.Ordinal)
                        && (pair.Value.Events > 0L || pair.Value.Units > 0L))
                    {
                        itemCatches[pair.Key.Content.Value] = pair.Value;
                    }
                    else if (string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.FishingNpcCatchStatisticId,
                        StringComparison.Ordinal)
                        && pair.Value.Events > 0L)
                    {
                        npcCatches[pair.Key.Content.Value] = pair.Value.Events;
                    }
                }
            }

            List<StatisticsListItem> catches =
                new List<StatisticsListItem>(itemCatches.Count + npcCatches.Count);
            foreach (KeyValuePair<StableContentKey, StatisticMetrics> pair in itemCatches)
            {
                catches.Add(CreateFishingItemCatchItem(pair.Key, pair.Value));
            }

            foreach (KeyValuePair<StableContentKey, long> pair in npcCatches)
            {
                catches.Add(CreateNpcStatisticsItem(
                    pair.Key,
                    pair.Value,
                    rateUnits: 0L,
                    rateAmount: 0L));
            }
            return catches;
        }

        /// <summary>
        /// 创建鱼饵页的唯一一级分组，列布局与弹药页一致。
        /// </summary>
        private static IEnumerable<StatisticsListGroup> CreateBaitGroups(StatisticsData data)
        {
            return new[]
            {
                new StatisticsListGroup(
                    "Mods.ComprehensiveStatistics.UI.Bait.UsageGroup",
                    CreateBaitItems(data),
                    nameIsLocalizationKey: true,
                    metricDisplay: StatisticsMetricDisplay.AmmoActivity)
            };
        }

        /// <summary>
        /// 创建按具体物品合并的鱼饵应耗、实耗与消耗率明细。
        /// </summary>
        private static List<StatisticsListItem> CreateBaitItems(StatisticsData data)
        {
            List<StatisticsListItem> items = new List<StatisticsListItem>();
            if (data == null)
            {
                return items;
            }

            foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in data.Entries)
            {
                if (!pair.Key.Content.HasValue
                    || pair.Value.IsZero
                    || !string.Equals(
                        pair.Key.StatisticId,
                        StatisticKeys.FishingBaitUsageStatisticId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                StableContentKey content = pair.Key.Content.Value;
                bool resolved = content.TryResolveItemType(out int itemType);
                StatisticsContentSource source = GetContentSource(content);
                string name;
                Asset<Texture2D> icon = null;
                if (resolved)
                {
                    Item sample = ContentSamples.ItemsByType[itemType];
                    name = Language.GetTextValue(
                        "Mods.ComprehensiveStatistics.UI.Bait.NameFormat",
                        Lang.GetItemNameValue(itemType),
                        Math.Max(0, sample?.bait ?? 0));
                    Main.instance.LoadItem(itemType);
                    icon = TextureAssets.Item[itemType];
                }
                else
                {
                    name = Language.GetTextValue(
                        "Mods.ComprehensiveStatistics.UI.Bait.MissingItemContent",
                        content.ToString());
                }

                items.Add(new StatisticsListItem(
                    name,
                    itemType: resolved ? itemType : ItemID.None,
                    icon: icon,
                    events: pair.Value.Events,
                    units: pair.Value.Units,
                    amount: pair.Value.Amount,
                    source: source,
                    iconKind: resolved
                        ? StatisticsIconKind.Item
                        : StatisticsIconKind.None,
                    content: content,
                    vanillaContentId: resolved
                        && source == StatisticsContentSource.Vanilla
                            ? itemType
                            : null));
            }

            return items;
        }

        /// <summary>
        /// 获取液体分类行使用的原版无底桶图标。
        /// </summary>
        private static int GetFishingLiquidIconItemType(FishingLiquidKind liquid)
        {
            return liquid switch
            {
                FishingLiquidKind.Honey => ItemID.BottomlessHoneyBucket,
                FishingLiquidKind.Lava => ItemID.BottomlessLavaBucket,
                _ => ItemID.BottomlessBucket
            };
        }

        /// <summary>
        /// 将物品渔获稳定键转换为带图标、事件数和实际数量的列表项。
        /// </summary>
        private static StatisticsListItem CreateFishingItemCatchItem(
            StableContentKey content,
            StatisticMetrics metrics)
        {
            bool resolved = content.TryResolveItemType(out int itemType);
            StatisticsContentSource source = GetContentSource(content);
            string name = resolved
                ? Lang.GetItemNameValue(itemType)
                : Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Fishing.MissingItemContent",
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
                source: source,
                iconKind: resolved ? StatisticsIconKind.Item : StatisticsIconKind.None,
                content: content,
                vanillaContentId: resolved && source == StatisticsContentSource.Vanilla
                    ? itemType
                    : null,
                rateUnits: 0L,
                rateAmount: 0L);
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
