using System;
using System.Collections.Generic;
using System.Linq;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>通用子页切换栏，按本地化文本宽度自动换行，并通过高度通知下方列表让位。</summary>
    internal sealed class StatisticsSubTabBar : UIElement
    {
        private readonly List<(StatisticsSubTab Page, UIPanel Button, UIText Text)> buttons = new();
        private string selected;
        private float lastWidth = -1f;
        internal event Action LayoutChanged;

        /// <summary>为注册的主页面创建子页按钮，选择通过稳定子页身份回传。</summary>
        internal StatisticsSubTabBar(IReadOnlyList<StatisticsSubTab> pages, Action<string> select)
        {
            Width.Set(0f, 1f);
            foreach (var page in pages)
            {
                var button = new UIPanel();
                button.SetPadding(2f);
                button.Height.Set(28f, 0f);
                var text = new UIText(Language.GetText(page.LabelKey), 0.78f);
                text.HAlign = text.VAlign = 0.5f;
                text.IgnoresMouseInteraction = true;
                text.DynamicallyScaleDownToWidth = true;
                button.Append(text);
                button.OnLeftClick += (_, _) => select(page.Id);
                buttons.Add((page, button, text));
                Append(button);
            }
        }

        /// <summary>切换选中项，并强制重测当前语言的按钮宽度。</summary>
        internal void Select(string id) { selected = id; lastWidth = -1f; }

        /// <summary>响应逻辑宽度及语言变化；布局不使用物理像素。</summary>
        internal void RefreshLayout()
        {
            float width = Math.Max(1f, GetInnerDimensions().Width);
            if (lastWidth == width) return;
            lastWidth = width;
            var slots = StatisticsSubTabLayout.Calculate(width, buttons.Select(entry =>
                FontAssets.MouseText.Value.MeasureString(Language.GetTextValue(entry.Page.LabelKey)).X * 0.78f).ToArray());
            for (int i = 0; i < buttons.Count; i++)
            {
                var entry = buttons[i];
                var slot = slots[i];
                entry.Text.SetText(Language.GetText(entry.Page.LabelKey));
                entry.Button.Left.Set(slot.X, 0f); entry.Button.Top.Set(slot.Y, 0f);
                entry.Button.Width.Set(slot.Width, 0f);
                entry.Text.Width.Set(-8f, 1f);
            }
            Height.Set(slots.Length == 0 ? 0f : slots[^1].Y + StatisticsSubTabLayout.ButtonHeight, 0f);
            Recalculate();
            LayoutChanged?.Invoke();
        }

        public override void Update(GameTime gameTime)
        {
            RefreshLayout();
            foreach (var entry in buttons)
            {
                bool active = entry.Page.Id == selected;
                entry.Button.BackgroundColor = active ? new Color(73, 96, 178, 245)
                    : entry.Button.IsMouseHovering ? new Color(52, 69, 128, 235) : new Color(38, 49, 91, 225);
                entry.Button.BorderColor = active ? new Color(130, 158, 238) : new Color(65, 84, 150);
                entry.Text.TextColor = active ? Color.White : new Color(190, 200, 225);
            }
            base.Update(gameTime);
        }
    }
}
