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
        private const float ToolbarTop = 138f;
        private const float ToolbarHeight = 62f;
        private const float ListTop = 210f;

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
        private UIText contentTitle;
        private UIText contentDescription;
        private UIText statisticsStartedAt;
        private UIText persistenceTestCounter;
        private UIText deathTotal;
        private StatisticsToolbar toolbar;
        private VirtualizedStatisticsList demoList;
        private UIImageButton closeButton;
        private int selectedTabIndex;
        private Point lastViewportSize;
        private bool isDemoListAttached;
        private bool isDeathTotalAttached;

        internal bool IsWritingSearchText => toolbar?.IsWritingSearchText == true;

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

            UIText title = new UIText(Language.GetText("Mods.ComprehensiveStatistics.UI.WindowTitle"), 1.15f);
            title.Left.Set(4f, 0f);
            title.Top.Set(4f, 0f);
            window.Append(title);

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

            contentDescription = new UIText(string.Empty, 0.9f);
            contentDescription.Left.Set(2f, 0f);
            contentDescription.Top.Set(38f, 0f);
            contentDescription.Width.Set(-4f, 1f);
            contentDescription.IsWrapped = true;
            contentDescription.TextColor = new Color(190, 200, 225);
            contentPanel.Append(contentDescription);

            statisticsStartedAt = new UIText(string.Empty, 0.78f);
            statisticsStartedAt.Left.Set(2f, 0f);
            statisticsStartedAt.Top.Set(92f, 0f);
            statisticsStartedAt.TextColor = new Color(151, 170, 214);
            contentPanel.Append(statisticsStartedAt);

            persistenceTestCounter = new UIText(string.Empty, 0.78f);
            persistenceTestCounter.Left.Set(2f, 0f);
            persistenceTestCounter.Top.Set(112f, 0f);
            persistenceTestCounter.TextColor = new Color(235, 190, 105);
            contentPanel.Append(persistenceTestCounter);

            deathTotal = new UIText(string.Empty, 1.05f);
            deathTotal.Left.Set(8f, 0f);
            deathTotal.Top.Set(ListTop + 12f, 0f);
            deathTotal.Width.Set(-16f, 1f);
            deathTotal.IsWrapped = true;
            deathTotal.TextColor = new Color(232, 237, 255);

            demoList = CreateDemoList();
            demoList.Top.Set(ListTop, 0f);
            demoList.Width.Set(0f, 1f);
            demoList.Height.Set(-ListTop, 1f);

            toolbar = new StatisticsToolbar(
                HandleScopeChanged,
                HandleFiltersChanged,
                HandleSortChanged);
            toolbar.Top.Set(ToolbarTop, 0f);
            toolbar.Width.Set(0f, 1f);
            toolbar.Height.Set(ToolbarHeight, 0f);
            contentPanel.Append(toolbar);

            SelectTab(0, playSound: false);
            HandleFiltersChanged(
                string.Empty,
                hideZeroValues: true,
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
            // A4 临时列表仍只用于物品页；B3 的正式死亡总数单独挂载到生存页。
            SetDemoListVisible(tabIndex == ItemsTabIndex);
            SetDeathTotalVisible(tabIndex == SurvivalTabIndex);
            UpdateTabColors();

            if (changed && playSound)
            {
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
        }

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

        private static LocalizedText GetTabText(int tabIndex, string field)
        {
            return Language.GetText($"Mods.ComprehensiveStatistics.UI.Tabs.{TabLocalizationKeys[tabIndex]}.{field}");
        }

        private void HandleScopeChanged(StatisticsViewScope scope)
        {
            UpdatePageHeading();
        }

        private void HandleFiltersChanged(
            string searchQuery,
            bool hideZeroValues,
            StatisticsContentSourceFilter sourceFilter)
        {
            demoList.SetFilters(searchQuery, hideZeroValues, sourceFilter);
        }

        private void HandleSortChanged(StatisticsListSortMode sortMode)
        {
            demoList.SetSortMode(sortMode);
        }

        private void UpdatePageHeading()
        {
            StatisticsViewScope scope = toolbar?.Scope ?? StatisticsViewScope.CharacterCareer;
            contentTitle.SetText(Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.Toolbar.ScopedTitleFormat",
                GetTabText(selectedTabIndex, "Title").Value,
                StatisticsToolbar.GetScopeText(scope).Value));
            contentDescription.SetText(Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.Toolbar.ScopedDescriptionFormat",
                StatisticsToolbar.GetScopeDescription(scope).Value,
                GetTabText(selectedTabIndex, "Description").Value));
            UpdateStatisticsStartedAt(scope);
        }

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

            UpdatePersistenceTestCounter(scope);
            UpdateDeathTotal(scope);
        }

        private void UpdatePersistenceTestCounter(StatisticsViewScope scope)
        {
            if (Main.gameMenu
                || Main.LocalPlayer?.active != true
                || scope == StatisticsViewScope.CurrentWorldPublic)
            {
                persistenceTestCounter.SetText(string.Empty);
                return;
            }

            StatisticsPlayer statisticsPlayer = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            StatisticsData scopeData = scope == StatisticsViewScope.CharacterCurrentWorld
                ? statisticsPlayer.CurrentWorldSlice
                : statisticsPlayer.Data?.Career;
            long worldEntries = scopeData?
                .GetValue(InternalStatisticKeys.B2WorldEntries).Events ?? 0L;
            persistenceTestCounter.SetText(Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.B2PersistenceTestCounter",
                worldEntries));
        }

        private void UpdateDeathTotal(StatisticsViewScope scope)
        {
            if (selectedTabIndex != SurvivalTabIndex)
            {
                return;
            }

            if (scope == StatisticsViewScope.CurrentWorldPublic)
            {
                deathTotal.SetText(
                    Language.GetText("Mods.ComprehensiveStatistics.UI.Survival.DeathTotalUnavailable"));
                return;
            }

            StatisticsPlayer statisticsPlayer = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            StatisticsData scopeData = scope == StatisticsViewScope.CharacterCurrentWorld
                ? statisticsPlayer.CurrentWorldSlice
                : statisticsPlayer.Data?.Career;
            long totalDeaths = scopeData?.GetValue(StatisticKeys.PlayerDeaths).Events ?? 0L;
            deathTotal.SetText(Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.Survival.DeathTotal",
                totalDeaths));
        }

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

        private void SetDeathTotalVisible(bool visible)
        {
            if (visible == isDeathTotalAttached)
            {
                if (visible)
                {
                    UpdateDeathTotal(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
                }

                return;
            }

            if (visible)
            {
                contentPanel.Append(deathTotal);
                UpdateDeathTotal(toolbar?.Scope ?? StatisticsViewScope.CharacterCareer);
            }
            else
            {
                deathTotal.Remove();
            }

            isDeathTotalAttached = visible;
            contentPanel.Recalculate();
        }

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

                // 每 17 项保留一个零值，确保“隐藏零值”开关可直接验收。
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

        private static void CloseWindow(UIMouseEvent evt, UIElement listeningElement)
        {
            ModContent.GetInstance<StatisticsUISystem>().Close();
        }
    }
}
