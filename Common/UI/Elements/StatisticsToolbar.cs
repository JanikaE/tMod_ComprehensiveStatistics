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
    /// 角色统计查看范围；世界公共数据由需要它的页面直接并列表达。
    /// </summary>
    internal enum StatisticsViewScope
    {
        CharacterCareer,
        CharacterCurrentWorld
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
    /// 所有统计页共用的工具栏，集中维护范围、搜索与内容来源筛选状态。
    /// </summary>
    internal sealed class StatisticsToolbar : UIElement
    {
        private const float RowHeight = 28f;
        private const float ControlGap = 5f;
        private const float SearchClearButtonWidth = 18f;
        private const float SearchClearButtonGap = 4f;

        private readonly Action<StatisticsViewScope> scopeChanged;
        private readonly Action<string, StatisticsContentSourceFilter> filtersChanged;
        private readonly ToolbarButton scopeButton;
        private readonly UIPanel searchPanel;
        private readonly UISearchBar searchBar;
        private readonly UIText searchPlaceholder;
        private readonly UIPanel clearSearchButton;
        private readonly UIText clearSearchText;
        private readonly ToolbarButton sourceButton;

        private StatisticsViewScope scope = StatisticsViewScope.CharacterCareer;
        private StatisticsContentSourceFilter sourceFilter = StatisticsContentSourceFilter.All;
        private string searchQuery = string.Empty;
        private float lastWidth = -1f;

        /// <summary>
        /// 创建工具栏并绑定范围与筛选状态变化回调。
        /// </summary>
        internal StatisticsToolbar(
            Action<StatisticsViewScope> scopeChanged,
            Action<string, StatisticsContentSourceFilter> filtersChanged)
        {
            this.scopeChanged = scopeChanged;
            this.filtersChanged = filtersChanged;

            Height.Set(RowHeight, 0f);

            scopeButton = new ToolbarButton(CycleScope);
            scopeButton.Top.Set(0f, 0f);
            Append(scopeButton);

            searchPanel = new UIPanel();
            searchPanel.Top.Set(0f, 0f);
            searchPanel.Height.Set(RowHeight, 0f);
            searchPanel.SetPadding(5f);
            searchPanel.BackgroundColor = new Color(20, 27, 52, 235);
            searchPanel.BorderColor = new Color(75, 97, 174, 255);
            // UISearchBar 会在自身下方绘制中文输入法的组合串与候选面板。
            // 父面板不能裁剪子元素，否则拼音字母和候选项会全部不可见。
            searchPanel.OverflowHidden = false;
            searchPanel.OnLeftClick += StartSearchInput;
            Append(searchPanel);

            searchBar = new UISearchBar(
                LocalizedText.Empty,
                0.72f);
            searchBar.Width.Set(
                -(SearchClearButtonWidth + SearchClearButtonGap),
                1f);
            searchBar.Height.Set(0f, 1f);
            searchBar.OnContentsChanged += SearchContentsChanged;
            searchPanel.Append(searchBar);

            // UISearchBar 的内置占位文本会在模组热重载后停留为空，直到内容首次变化。
            // 独立提示层只依赖本模组保存的查询状态，因此空查询始终能立即显示提示。
            searchPlaceholder = new UIText(string.Empty, 0.72f);
            searchPlaceholder.Left.Set(3f, 0f);
            searchPlaceholder.Width.Set(
                -(SearchClearButtonWidth + SearchClearButtonGap + 3f),
                1f);
            searchPlaceholder.Height.Set(0f, 1f);
            searchPlaceholder.VAlign = 0.5f;
            searchPlaceholder.TextColor = new Color(130, 144, 184);
            searchPlaceholder.DynamicallyScaleDownToWidth = true;
            searchPlaceholder.IgnoresMouseInteraction = true;
            searchPanel.Append(searchPlaceholder);

            // 占位层必须位于 UISearchBar 后方；中文组合串由 UISearchBar 绘制，
            // 若占位层在其上方会再次遮住尚未提交的拼音文本。
            searchBar.Remove();
            searchPanel.Append(searchBar);

            clearSearchButton = new UIPanel();
            clearSearchButton.Left.Set(-SearchClearButtonWidth, 1f);
            clearSearchButton.Width.Set(SearchClearButtonWidth, 0f);
            clearSearchButton.Height.Set(0f, 1f);
            clearSearchButton.SetPadding(0f);
            clearSearchButton.BorderColor = new Color(75, 97, 174, 255);
            clearSearchButton.OnLeftClick += ClearSearch;
            searchPanel.Append(clearSearchButton);

            clearSearchText = new UIText("×", 0.72f);
            clearSearchText.HAlign = 0.5f;
            clearSearchText.VAlign = 0.5f;
            clearSearchText.IgnoresMouseInteraction = true;
            clearSearchButton.Append(clearSearchText);

            sourceButton = new ToolbarButton(CycleSourceFilter);
            sourceButton.Top.Set(0f, 0f);
            Append(sourceButton);

        }

        internal StatisticsViewScope Scope => scope;

        internal bool IsWritingSearchText => searchBar.IsWritingText;

        public override void OnActivate()
        {
            base.OnActivate();

            // ModSystem.Load 中的预初始化早于本地化注册；真正挂载界面时必须重新解析所有文本。
            RefreshLocalizedText();
            searchBar.SetContents(searchQuery, forced: true);
        }

        /// <summary>
        /// 在本地化表可用后刷新按钮与搜索占位文本，供窗口实际打开时显式调用。
        /// </summary>
        internal void RefreshLocalizedText()
        {
            RefreshButtonLabels();
            RefreshSearchVisuals();
        }

        public override void Update(GameTime gameTime)
        {
            float width = GetInnerDimensions().Width;
            if (Math.Abs(width - lastWidth) > 0.5f)
            {
                lastWidth = width;
                UpdateControlLayout(width);
            }

            bool canClearSearch = searchQuery.Length > 0;
            clearSearchButton.BackgroundColor = canClearSearch
                && clearSearchButton.IsMouseHovering
                    ? new Color(63, 82, 151, 245)
                    : new Color(36, 47, 88, 225);
            clearSearchText.TextColor = canClearSearch
                ? new Color(220, 228, 248)
                : new Color(93, 105, 142);

            base.Update(gameTime);
        }

        /// <summary>
        /// 根据可用宽度依次排列角色范围、内容来源和搜索控件。
        /// </summary>
        private void UpdateControlLayout(float width)
        {
            // 两个按钮都同时展示全部可选项，因此按文本数量分配宽度，并为搜索框保留空间。
            float scopeWidth = MathHelper.Clamp(width * 0.36f, 130f, 250f);
            float sourceWidth = MathHelper.Clamp(width * 0.4f, 145f, 280f);
            float availableButtonWidth = MathHelper.Max(
                2f,
                width - ControlGap * 2f - 70f);
            float totalButtonWidth = scopeWidth + sourceWidth;
            if (totalButtonWidth > availableButtonWidth)
            {
                float scale = availableButtonWidth / totalButtonWidth;
                scopeWidth *= scale;
                sourceWidth *= scale;
            }

            scopeButton.Left.Set(0f, 0f);
            scopeButton.Width.Set(scopeWidth, 0f);

            float sourceLeft = scopeWidth + ControlGap;
            sourceButton.Left.Set(sourceLeft, 0f);
            sourceButton.Width.Set(sourceWidth, 0f);

            float searchLeft = sourceLeft + sourceWidth + ControlGap;
            searchPanel.Left.Set(searchLeft, 0f);
            searchPanel.Width.Set(-searchLeft, 1f);
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
            searchQuery = contents ?? string.Empty;
            RefreshSearchVisuals();
            NotifyFiltersChanged();
        }

        /// <summary>
        /// 清空搜索内容，并立即恢复占位提示和未搜索的列表结果。
        /// </summary>
        private void ClearSearch(UIMouseEvent evt, UIElement listeningElement)
        {
            if (searchQuery.Length == 0)
            {
                return;
            }

            searchBar.SetContents(string.Empty, forced: true);
            // 当前 tModLoader 会通过 OnContentsChanged 回调同步状态；此兜底保证未来
            // SetContents 行为变化时，清空按钮仍不会留下陈旧过滤条件。
            if (searchQuery.Length > 0)
            {
                SearchContentsChanged(string.Empty);
            }

            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        /// <summary>
        /// 根据当前查询刷新稳定占位文本；不依赖 UISearchBar 的热重载内部状态。
        /// </summary>
        private void RefreshSearchVisuals()
        {
            searchPlaceholder.SetText(searchQuery.Length == 0
                ? Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.Toolbar.SearchHint")
                : string.Empty);
        }

        /// <summary>
        /// 在角色生涯和角色当前世界之间循环切换。
        /// </summary>
        private void CycleScope()
        {
            StopSearchInput();
            scope = scope == StatisticsViewScope.CharacterCareer
                ? StatisticsViewScope.CharacterCurrentWorld
                : StatisticsViewScope.CharacterCareer;

            RefreshButtonLabels();
            scopeChanged(scope);
        }

        /// <summary>
        /// 使用 Terraria 颜色标记生成一个范围选项；选中项使用高亮字体色。
        /// </summary>
        private static string FormatSelectableOption(string label, bool selected)
        {
            string color = selected ? "EEF1FF" : "8290B8";
            return $"[c/{color}:{label}]";
        }

        /// <summary>
        /// 组合角色范围按钮文字，使两个可选范围始终同时可见。
        /// </summary>
        private string GetCombinedScopeText()
        {
            return string.Concat(
                FormatSelectableOption(
                    GetScopeText(StatisticsViewScope.CharacterCareer).Value,
                    scope == StatisticsViewScope.CharacterCareer),
                " / ",
                FormatSelectableOption(
                    GetScopeText(StatisticsViewScope.CharacterCurrentWorld).Value,
                    scope == StatisticsViewScope.CharacterCurrentWorld));
        }

        /// <summary>
        /// 组合来源范围按钮文字，使三个可选来源始终同时可见。
        /// </summary>
        private string GetCombinedSourceText()
        {
            StatisticsContentSourceFilter[] filters =
            {
                StatisticsContentSourceFilter.All,
                StatisticsContentSourceFilter.Vanilla,
                StatisticsContentSourceFilter.Modded
            };
            string[] options = new string[filters.Length];
            for (int index = 0; index < filters.Length; index++)
            {
                StatisticsContentSourceFilter filter = filters[index];
                options[index] = FormatSelectableOption(
                    Language.GetTextValue(GetSourceLocalizationKey(filter)),
                    sourceFilter == filter);
            }

            return string.Join(" / ", options);
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
            scopeButton.SetText(GetCombinedScopeText());
            sourceButton.SetText(GetCombinedSourceText());
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
                _ => "Mods.ComprehensiveStatistics.UI.Toolbar.ScopeCareerDescription"
            });
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
                text.TextColor = new Color(130, 144, 184);
                text.DynamicallyScaleDownToWidth = true;
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
                BorderColor = new Color(75, 97, 174, 255);
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
