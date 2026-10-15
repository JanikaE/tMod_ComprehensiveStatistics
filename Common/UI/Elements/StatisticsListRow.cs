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
        private const float MixedNameRightReserve = 254f;
        private const float MixedEventsRight = 132f;

        private readonly UIText markerText;
        private readonly UIElement nameContainer;
        private readonly UIText nameText;
        private readonly UIText eventsText;
        private readonly UIText valueText;
        private Action clickAction;
        private Asset<Texture2D> icon;
        private int iconType;
        private StatisticsIconKind iconKind;
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

            eventsText = new UIText(string.Empty, 0.76f);
            eventsText.Left.Set(-MixedEventsRight, 0f);
            eventsText.HAlign = 1f;
            eventsText.VAlign = 0.5f;
            Append(eventsText);

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
            bool canExpand,
            long totalEvents,
            long totalUnits,
            long totalAmount,
            StatisticsMetricDisplay metricDisplay,
            int depth,
            Action onClick)
        {
            // 分组行不显示图标，左侧区域改为展开状态标记。
            isGroup = true;
            clickAction = canExpand ? onClick : null;
            iconType = 0;
            iconKind = StatisticsIconKind.None;
            markerText.Left.Set(MarkerLeft + depth * HierarchyIndent, 0f);
            markerText.SetText(canExpand ? (isExpanded ? "▼" : "▶") : string.Empty);
            markerText.TextColor = new Color(225, 232, 255);

            icon = null;

            SetNameIndent(depth, metricDisplay);
            nameText.SetText(name);
            nameText.TextColor = new Color(238, 241, 255);
            SetMetrics(totalUnits, totalEvents, totalAmount, metricDisplay);
            eventsText.TextColor = new Color(238, 241, 255);
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

            iconType = item.ItemType;
            iconKind = item.IconKind;
            icon = item.Icon;
            iconOffset = IconCenter + depth * HierarchyIndent;

            // 同一层级的分组与普通条目共用文字起点；箭头或图标只占左侧槽位。
            SetNameIndent(depth, metricDisplay);
            nameText.SetText(item.Name);
            nameText.TextColor = new Color(205, 214, 239);
            SetMetrics(item.Units, item.Events, item.Amount, metricDisplay);
            eventsText.TextColor = new Color(190, 200, 225);
            valueText.TextColor = new Color(190, 200, 225);
        }

        /// <summary>
        /// 清除复用行上的文字、图标与点击状态。
        /// </summary>
        internal void ClearBinding()
        {
            // 行池中暂时无对应数据的控件必须清空，避免残留旧内容或点击动作。
            clickAction = null;
            iconType = 0;
            iconKind = StatisticsIconKind.None;
            markerText.SetText(string.Empty);
            nameText.SetText(string.Empty);
            eventsText.SetText(string.Empty);
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

            if (isGroup || icon == null)
            {
                return;
            }

            // 只在虚拟化后的可见行访问 Value，使尚未出现过的内容贴图按需完成加载。
            Texture2D texture = icon.Value;
            Rectangle sourceRectangle = GetFirstIconFrame(texture, iconType, iconKind);
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
        /// 按物品、NPC 或 Buff 的贴图规则取得用于列表图标的第一帧。
        /// </summary>
        private static Rectangle GetFirstIconFrame(
            Texture2D texture,
            int contentType,
            StatisticsIconKind kind)
        {
            if (kind == StatisticsIconKind.Item)
            {
                return Main.itemAnimations[contentType]?.GetFrame(texture, 0) ?? texture.Bounds;
            }

            if (kind == StatisticsIconKind.Npc)
            {
                int frameCount = contentType >= 0 && contentType < Main.npcFrameCount.Length
                    ? Math.Max(1, Main.npcFrameCount[contentType])
                    : 1;
                return new Rectangle(0, 0, texture.Width, texture.Height / frameCount);
            }

            // Buff 图标是单帧贴图；未知类型也安全回退为完整贴图。
            return texture.Bounds;
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
        private void SetNameIndent(int depth, StatisticsMetricDisplay metricDisplay)
        {
            float left = NameLeft + depth * HierarchyIndent;
            nameContainer.Left.Set(left, 0f);
            float rightReserve = metricDisplay == StatisticsMetricDisplay.Mixed
                ? MixedNameRightReserve
                : NameRightReserve;
            nameContainer.Width.Set(-left - rightReserve, 1f);
        }

        /// <summary>
        /// 按页面口径设置一个或两个数值列；生存页同时展示死亡次数与掉血总量。
        /// </summary>
        private void SetMetrics(
            long units,
            long events,
            long amount,
            StatisticsMetricDisplay metricDisplay)
        {
            if (metricDisplay == StatisticsMetricDisplay.Mixed)
            {
                eventsText.SetText(events.ToString("N0"));
                valueText.SetText(amount.ToString("N0"));
                return;
            }

            eventsText.SetText(string.Empty);
            valueText.SetText(FormatMetrics(units, events, amount, metricDisplay));
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

            if (metricDisplay == StatisticsMetricDisplay.Distance)
            {
                // 存档以像素保存唯一真值，显示时按 Terraria 的 16 像素一格换算。
                return Language.GetTextValue(
                    "Mods.ComprehensiveStatistics.UI.List.DistanceFormat",
                    amount / 16d);
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
