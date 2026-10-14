using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>
    /// 统计查看范围；工具栏会根据当前 Tab 排除不适用的世界公共范围。
    /// </summary>
    internal enum StatisticsViewScope
    {
        CharacterCareer,
        CharacterCurrentWorld,
        CurrentWorldPublic
    }

    /// <summary>
    /// 工具栏提供的内容来源筛选状态。
    /// </summary>
    internal enum StatisticsContentSourceFilter
    {
        All,
        Vanilla,
        Modded
    }

    /// <summary>
    /// 所有统计页共用的工具栏，集中维护范围、搜索、排序与内容来源筛选状态。
    /// </summary>
    internal sealed class StatisticsToolbar : UIElement
    {
        private const float RowHeight = 28f;
        private const float RowGap = 6f;
        private const float ControlGap = 5f;

        private readonly Action<StatisticsViewScope> scopeChanged;
        private readonly Action<string, StatisticsContentSourceFilter> filtersChanged;
        private readonly Action<StatisticsListSortMode> sortChanged;
        private readonly ToolbarButton scopeButton;
        private readonly UIPanel searchPanel;
        private readonly UISearchBar searchBar;
        private readonly ToolbarButton sortButton;
        private readonly ToolbarButton sourceButton;

        private StatisticsViewScope scope = StatisticsViewScope.CharacterCareer;
        private StatisticsListSortMode sortMode = StatisticsListSortMode.ValueDescending;
        private StatisticsContentSourceFilter sourceFilter = StatisticsContentSourceFilter.All;
        private bool worldPublicScopeAvailable = true;
        private string searchQuery = string.Empty;
        private float lastWidth = -1f;

        /// <summary>
        /// 创建工具栏并绑定范围、筛选和排序状态变化回调。
        /// </summary>
        internal StatisticsToolbar(
            Action<StatisticsViewScope> scopeChanged,
            Action<string, StatisticsContentSourceFilter> filtersChanged,
            Action<StatisticsListSortMode> sortChanged)
        {
            this.scopeChanged = scopeChanged;
            this.filtersChanged = filtersChanged;
            this.sortChanged = sortChanged;

            Height.Set(RowHeight * 2f + RowGap, 0f);

            scopeButton = new ToolbarButton(CycleScope);
            scopeButton.Top.Set(0f, 0f);
            Append(scopeButton);

            searchPanel = new UIPanel();
            searchPanel.Top.Set(0f, 0f);
            searchPanel.Height.Set(RowHeight, 0f);
            searchPanel.SetPadding(5f);
            searchPanel.BackgroundColor = new Color(20, 27, 52, 235);
            searchPanel.BorderColor = new Color(75, 97, 174, 255);
            searchPanel.OverflowHidden = true;
            searchPanel.OnLeftClick += StartSearchInput;
            Append(searchPanel);

            searchBar = new UISearchBar(
                Language.GetText("Mods.ComprehensiveStatistics.UI.Toolbar.SearchHint"),
                0.72f);
            searchBar.Width.Set(0f, 1f);
            searchBar.Height.Set(0f, 1f);
            searchBar.OnContentsChanged += SearchContentsChanged;
            searchPanel.Append(searchBar);

            sortButton = new ToolbarButton(CycleSortMode);
            sortButton.Top.Set(RowHeight + RowGap, 0f);
            Append(sortButton);

            sourceButton = new ToolbarButton(CycleSourceFilter);
            sourceButton.Top.Set(RowHeight + RowGap, 0f);
            Append(sourceButton);

        }

        internal StatisticsViewScope Scope => scope;

        internal bool IsWritingSearchText => searchBar.IsWritingText;

        /// <summary>
        /// 设置当前页面是否允许选择世界公共统计范围。
        /// </summary>
        internal void SetWorldPublicScopeAvailable(bool available)
        {
            worldPublicScopeAvailable = available;
            if (!available && scope == StatisticsViewScope.CurrentWorldPublic)
            {
                // 切入纯角色统计页时不能保留一个该页无法通过按钮再次选中的范围。
                scope = StatisticsViewScope.CharacterCareer;
            }

            RefreshButtonLabels();
        }

        public override void OnActivate()
        {
            base.OnActivate();

            // ModSystem.Load 中的预初始化早于本地化注册；真正挂载界面时必须重新解析所有文本。
            RefreshButtonLabels();
            searchBar.SetContents(searchQuery, forced: true);
        }

        public override void Update(GameTime gameTime)
        {
            float width = GetInnerDimensions().Width;
            if (Math.Abs(width - lastWidth) > 0.5f)
            {
                lastWidth = width;
                UpdateControlLayout(width);
            }

            base.Update(gameTime);
        }

        /// <summary>
        /// 根据可用宽度重新分配两行工具栏控件。
        /// </summary>
        private void UpdateControlLayout(float width)
        {
            // 第一行给范围按钮约三分之一宽度；第二行由排序和来源筛选平分。
            float scopeWidth = MathHelper.Clamp(width * 0.34f, 96f, 190f);
            scopeWidth = MathHelper.Min(scopeWidth, MathHelper.Max(1f, width - ControlGap - 90f));
            scopeButton.Left.Set(0f, 0f);
            scopeButton.Width.Set(scopeWidth, 0f);

            searchPanel.Left.Set(scopeWidth + ControlGap, 0f);
            searchPanel.Width.Set(-(scopeWidth + ControlGap), 1f);

            float halfWidth = MathHelper.Max(1f, (width - ControlGap) / 2f);
            sortButton.Left.Set(0f, 0f);
            sortButton.Width.Set(halfWidth, 0f);
            sourceButton.Left.Set(halfWidth + ControlGap, 0f);
            sourceButton.Width.Set(halfWidth, 0f);
            Recalculate();
        }

        /// <summary>
        /// 点击搜索面板时进入文本输入状态。
        /// </summary>
        private void StartSearchInput(UIMouseEvent evt, UIElement listeningElement)
        {
            if (!searchBar.IsWritingText)
            {
                searchBar.ToggleTakingText();
            }
        }

        /// <summary>
        /// 保存搜索文本并立即通知列表刷新筛选结果。
        /// </summary>
        private void SearchContentsChanged(string contents)
        {
            searchQuery = contents;
            NotifyFiltersChanged();
        }

        /// <summary>
        /// 循环切换当前页面支持的统计查看范围。
        /// </summary>
        private void CycleScope()
        {
            StopSearchInput();
            scope = scope switch
            {
                StatisticsViewScope.CharacterCareer => StatisticsViewScope.CharacterCurrentWorld,
                StatisticsViewScope.CharacterCurrentWorld when worldPublicScopeAvailable =>
                    StatisticsViewScope.CurrentWorldPublic,
                _ => StatisticsViewScope.CharacterCareer
            };

            RefreshButtonLabels();
            scopeChanged(scope);
        }

        /// <summary>
        /// 在降序、升序和名称排序之间循环切换。
        /// </summary>
        private void CycleSortMode()
        {
            StopSearchInput();
            sortMode = sortMode switch
            {
                StatisticsListSortMode.ValueDescending => StatisticsListSortMode.ValueAscending,
                StatisticsListSortMode.ValueAscending => StatisticsListSortMode.Name,
                _ => StatisticsListSortMode.ValueDescending
            };

            RefreshButtonLabels();
            sortChanged(sortMode);
        }

        /// <summary>
        /// 在全部、原版和模组来源筛选之间循环切换。
        /// </summary>
        private void CycleSourceFilter()
        {
            StopSearchInput();
            sourceFilter = sourceFilter switch
            {
                StatisticsContentSourceFilter.All => StatisticsContentSourceFilter.Vanilla,
                StatisticsContentSourceFilter.Vanilla => StatisticsContentSourceFilter.Modded,
                _ => StatisticsContentSourceFilter.All
            };

            RefreshButtonLabels();
            NotifyFiltersChanged();
        }

        /// <summary>
        /// 将当前搜索文本和来源筛选作为一个状态通知给列表。
        /// </summary>
        private void NotifyFiltersChanged()
        {
            filtersChanged(searchQuery, sourceFilter);
        }

        /// <summary>
        /// 在操作其他工具栏控件前结束搜索框的文本输入状态。
        /// </summary>
        private void StopSearchInput()
        {
            if (searchBar.IsWritingText)
            {
                searchBar.ToggleTakingText();
            }
        }

        /// <summary>
        /// 根据当前状态和活动语言刷新所有按钮文字。
        /// </summary>
        private void RefreshButtonLabels()
        {
            scopeButton.SetText(GetScopeText(scope));
            sortButton.SetText(Language.GetTextValue(GetSortLocalizationKey(sortMode)));
            sourceButton.SetText(Language.GetTextValue(GetSourceLocalizationKey(sourceFilter)));
        }

        /// <summary>
        /// 获取指定查看范围的本地化短名称。
        /// </summary>
        internal static LocalizedText GetScopeText(StatisticsViewScope viewScope)
        {
            return Language.GetText(viewScope switch
            {
                StatisticsViewScope.CharacterCurrentWorld =>
                    "Mods.ComprehensiveStatistics.UI.Toolbar.ScopeCurrentWorld",
                StatisticsViewScope.CurrentWorldPublic =>
                    "Mods.ComprehensiveStatistics.UI.Toolbar.ScopeWorldPublic",
                _ => "Mods.ComprehensiveStatistics.UI.Toolbar.ScopeCareer"
            });
        }

        /// <summary>
        /// 获取指定查看范围的本地化说明文字。
        /// </summary>
        internal static LocalizedText GetScopeDescription(StatisticsViewScope viewScope)
        {
            return Language.GetText(viewScope switch
            {
                StatisticsViewScope.CharacterCurrentWorld =>
                    "Mods.ComprehensiveStatistics.UI.Toolbar.ScopeCurrentWorldDescription",
                StatisticsViewScope.CurrentWorldPublic =>
                    "Mods.ComprehensiveStatistics.UI.Toolbar.ScopeWorldPublicDescription",
                _ => "Mods.ComprehensiveStatistics.UI.Toolbar.ScopeCareerDescription"
            });
        }

        /// <summary>
        /// 获取指定排序方式的本地化键。
        /// </summary>
        private static string GetSortLocalizationKey(StatisticsListSortMode mode)
        {
            return mode switch
            {
                StatisticsListSortMode.ValueAscending =>
                    "Mods.ComprehensiveStatistics.UI.Toolbar.SortAscending",
                StatisticsListSortMode.Name =>
                    "Mods.ComprehensiveStatistics.UI.Toolbar.SortName",
                _ => "Mods.ComprehensiveStatistics.UI.Toolbar.SortDescending"
            };
        }

        /// <summary>
        /// 获取指定内容来源筛选的本地化键。
        /// </summary>
        private static string GetSourceLocalizationKey(StatisticsContentSourceFilter filter)
        {
            return filter switch
            {
                StatisticsContentSourceFilter.Vanilla =>
                    "Mods.ComprehensiveStatistics.UI.Toolbar.SourceVanilla",
                StatisticsContentSourceFilter.Modded =>
                    "Mods.ComprehensiveStatistics.UI.Toolbar.SourceModded",
                _ => "Mods.ComprehensiveStatistics.UI.Toolbar.SourceAll"
            };
        }

        /// <summary>
        /// 工具栏中的统一文字按钮，负责公共尺寸、颜色和点击音效。
        /// </summary>
        private sealed class ToolbarButton : UIPanel
        {
            private readonly Action clickAction;
            private readonly UIText text;

            /// <summary>
            /// 创建执行指定无参数操作的工具栏按钮。
            /// </summary>
            internal ToolbarButton(Action clickAction)
            {
                this.clickAction = clickAction;
                Height.Set(RowHeight, 0f);
                SetPadding(3f);
                OverflowHidden = true;
                BorderColor = new Color(75, 97, 174, 255);
                OnLeftClick += HandleClick;

                text = new UIText(string.Empty, 0.68f);
                text.HAlign = 0.5f;
                text.VAlign = 0.5f;
                Append(text);
            }

            /// <summary>
            /// 使用可随语言切换更新的本地化文本设置按钮内容。
            /// </summary>
            internal void SetText(LocalizedText value)
            {
                text.SetText(value);
            }

            /// <summary>
            /// 使用已经解析的字符串设置按钮内容。
            /// </summary>
            internal void SetText(string value)
            {
                text.SetText(value);
            }

            public override void Update(GameTime gameTime)
            {
                BackgroundColor = IsMouseHovering
                    ? new Color(63, 82, 151, 245)
                    : new Color(45, 59, 110, 235);
                base.Update(gameTime);
            }

            /// <summary>
            /// 执行按钮操作并播放统一点击音效。
            /// </summary>
            private void HandleClick(UIMouseEvent evt, UIElement listeningElement)
            {
                clickAction();
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
        }
    }
}
