using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
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
        private const float HierarchyIndent = 28f;
        private const float MarkerLeft = 2f;
        private const float IconCenter = 14f;
        private const float NameLeft = 28f;
        private const float NameRightReserve = 138f;

        private readonly UIText markerText;
        private readonly UIElement nameContainer;
        private readonly UIText nameText;
        private readonly UIText valueText;
        private Action clickAction;
        private Asset<Texture2D> icon;
        private int itemType;
        private bool isGroup;
        private float iconOffset = IconCenter;

        /// <summary>
        /// 创建可在分组与叶子项之间复用的统计列表行。
        /// </summary>
        internal StatisticsListRow()
        {
            SetPadding(4f);
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

        /// <summary>
        /// 将当前行绑定为指定层级的可折叠分组。
        /// </summary>
        internal void BindGroup(
            string name,
            bool isExpanded,
            long totalEvents,
            long totalUnits,
            long totalAmount,
            StatisticsMetricDisplay metricDisplay,
            int depth,
            Action onClick)
        {
            // 分组行不显示图标，左侧区域改为展开状态标记。
            isGroup = true;
            clickAction = onClick;
            itemType = 0;
            markerText.Left.Set(MarkerLeft + depth * HierarchyIndent, 0f);
            markerText.SetText(isExpanded ? "▼" : "▶");
            markerText.TextColor = new Color(225, 232, 255);

            icon = null;

            SetNameIndent(depth);
            nameText.SetText(name);
            nameText.TextColor = new Color(238, 241, 255);
            valueText.SetText(FormatMetrics(totalUnits, totalEvents, totalAmount, metricDisplay));
            valueText.TextColor = new Color(238, 241, 255);
        }

        /// <summary>
        /// 将当前行绑定为指定层级的统计叶子项。
        /// </summary>
        internal void BindItem(
            StatisticsListItem item,
            StatisticsMetricDisplay metricDisplay,
            int depth)
        {
            // 子项恢复图标缩进，并清除行池上一次绑定的分组点击动作。
            isGroup = false;
            clickAction = null;
            markerText.SetText(string.Empty);

            itemType = item.ItemType;
            icon = item.Icon;
            iconOffset = IconCenter + depth * HierarchyIndent;

            // 同一层级的分组与普通条目共用文字起点；箭头或图标只占左侧槽位。
            SetNameIndent(depth);
            nameText.SetText(item.Name);
            nameText.TextColor = new Color(205, 214, 239);
            valueText.SetText(FormatMetrics(item.Units, item.Events, item.Amount, metricDisplay));
            valueText.TextColor = new Color(190, 200, 225);
        }

        /// <summary>
        /// 清除复用行上的文字、图标与点击状态。
        /// </summary>
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
            // UIPanel 的九宫格角块在列表顶部被裁剪时会残留一个小方块；
            // 统计行只需要纯色底，因此直接绘制矩形，不调用 UIPanel 的九宫格绘制。
            CalculatedStyle dimensions = GetDimensions();
            Rectangle backgroundRectangle = new Rectangle(
                (int)dimensions.X,
                (int)dimensions.Y,
                (int)dimensions.Width,
                (int)dimensions.Height);
            spriteBatch.Draw(
                TextureAssets.MagicPixel.Value,
                backgroundRectangle,
                BackgroundColor);

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
            Vector2 iconCenter = new Vector2(
                dimensions.X + iconOffset,
                dimensions.Y + dimensions.Height * 0.5f);

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

        /// <summary>
        /// 将行点击转发给当前绑定分组的操作。
        /// </summary>
        private void HandleClick(UIMouseEvent evt, UIElement listeningElement)
        {
            clickAction?.Invoke();
        }

        /// <summary>
        /// 按树层级设置统一的名称起点和剩余可用宽度。
        /// </summary>
        private void SetNameIndent(int depth)
        {
            float left = NameLeft + depth * HierarchyIndent;
            nameContainer.Left.Set(left, 0f);
            nameContainer.Width.Set(-left - NameRightReserve, 1f);
        }

        /// <summary>
        /// 按当前页面口径格式化行右侧的统计值。
        /// </summary>
        private static string FormatMetrics(
            long units,
            long events,
            long amount,
            StatisticsMetricDisplay metricDisplay)
        {
            if (metricDisplay == StatisticsMetricDisplay.Amount)
            {
                return Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.List.AmountFormat",
                    amount);
            }

            if (metricDisplay == StatisticsMetricDisplay.Events)
            {
                return Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.List.EventsFormat",
                    events);
            }

            return Language.GetTextValue(
                "Mods.ComprehensiveStatistics.UI.List.MetricFormat",
                units,
                events);
        }
    }
}
