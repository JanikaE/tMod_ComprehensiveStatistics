using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>
    /// 可被虚拟列表反复绑定的单行控件，同时承担分组行和普通子项两种外观。
    /// </summary>
    internal sealed class StatisticsListRow : UIPanel
    {
        private readonly UIText markerText;
        private readonly UIElement nameContainer;
        private readonly UIText nameText;
        private readonly UIText valueText;
        private Action clickAction;
        private Asset<Texture2D> icon;
        private int itemType;
        private bool isGroup;

        internal StatisticsListRow()
        {
            SetPadding(4f);
            BorderColor = Color.Transparent;
            OnLeftClick += HandleClick;

            markerText = new UIText(string.Empty, 0.78f);
            markerText.Left.Set(2f, 0f);
            markerText.VAlign = 0.5f;
            Append(markerText);

            nameContainer = new UIElement();
            nameContainer.Left.Set(52f, 0f);
            nameContainer.Width.Set(-190f, 1f);
            nameContainer.Height.Set(0f, 1f);
            nameContainer.OverflowHidden = true;
            Append(nameContainer);

            nameText = new UIText(string.Empty, 0.78f);
            nameText.VAlign = 0.5f;
            nameContainer.Append(nameText);

            valueText = new UIText(string.Empty, 0.76f);
            valueText.Left.Set(-4f, 0f);
            valueText.HAlign = 1f;
            valueText.VAlign = 0.5f;
            Append(valueText);
        }

        internal void BindGroup(
            string name,
            bool isExpanded,
            long totalEvents,
            long totalUnits,
            Action onClick)
        {
            // 分组行不显示图标，左侧区域改为展开状态标记。
            isGroup = true;
            clickAction = onClick;
            itemType = 0;
            markerText.SetText(isExpanded ? "▼" : "▶");
            markerText.TextColor = new Color(225, 232, 255);

            icon = null;

            nameContainer.Left.Set(24f, 0f);
            nameContainer.Width.Set(-162f, 1f);
            nameText.SetText(name);
            nameText.TextColor = new Color(238, 241, 255);
            valueText.SetText(FormatMetrics(totalUnits, totalEvents));
            valueText.TextColor = new Color(238, 241, 255);
        }

        internal void BindItem(StatisticsListItem item)
        {
            // 子项恢复图标缩进，并清除行池上一次绑定的分组点击动作。
            isGroup = false;
            clickAction = null;
            markerText.SetText(string.Empty);

            itemType = item.ItemType;
            icon = item.Icon;

            nameContainer.Left.Set(52f, 0f);
            nameContainer.Width.Set(-190f, 1f);
            nameText.SetText(item.Name);
            nameText.TextColor = new Color(205, 214, 239);
            valueText.SetText(FormatMetrics(item.Units, item.Events));
            valueText.TextColor = new Color(190, 200, 225);
        }

        internal void ClearBinding()
        {
            // 行池中暂时无对应数据的控件必须清空，避免残留旧内容或点击动作。
            clickAction = null;
            itemType = 0;
            markerText.SetText(string.Empty);
            nameText.SetText(string.Empty);
            valueText.SetText(string.Empty);
            icon = null;
        }

        public override void Update(GameTime gameTime)
        {
            if (isGroup)
            {
                BackgroundColor = IsMouseHovering
                    ? new Color(67, 86, 157, 245)
                    : new Color(52, 67, 123, 235);
            }
            else
            {
                BackgroundColor = IsMouseHovering
                    ? new Color(43, 56, 105, 235)
                    : new Color(29, 38, 72, 215);
            }

            base.Update(gameTime);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            base.DrawSelf(spriteBatch);

            if (isGroup || icon?.IsLoaded != true)
            {
                return;
            }

            Texture2D texture = icon.Value;
            // 动画物品的源纹理包含多帧；统计列表固定展示第一帧。
            Rectangle sourceRectangle = Main.itemAnimations[itemType]?.GetFrame(texture, 0) ?? texture.Bounds;
            float scale = MathHelper.Min(
                24f / sourceRectangle.Width,
                24f / sourceRectangle.Height);
            CalculatedStyle dimensions = GetDimensions();
            Vector2 iconCenter = new Vector2(dimensions.X + 34f, dimensions.Y + dimensions.Height * 0.5f);

            spriteBatch.Draw(
                texture,
                iconCenter,
                sourceRectangle,
                Color.White,
                0f,
                sourceRectangle.Size() * 0.5f,
                scale,
                SpriteEffects.None,
                0f);
        }

        private void HandleClick(UIMouseEvent evt, UIElement listeningElement)
        {
            clickAction?.Invoke();
        }

        private static string FormatMetrics(long units, long events)
        {
            return Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.List.MetricFormat",
                units,
                events);
        }
    }
}
