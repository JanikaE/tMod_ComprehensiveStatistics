using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
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
        private const float GroupIconAfterMarkerOffset = 14f;
        private const float GroupNameAfterMarkerOffset = 16f;
        private const float NameRightReserve = 138f;
        private const float MixedNameRightReserve = 254f;
        private const float MixedEventsRight = 132f;
        private const float ItemActivityNameRightReserve = 370f;
        private const float ItemActivityPickupUnitsRight = 236f;
        private const float ItemActivityUseEventsRight = 120f;
        private const float EconomyCoinIconSize = 18f;

        // 经济页的四种币图标使用固定槽位；数字右对齐到对应图标左侧，使不同位数的
        // 金额不会推动图标位置。偏移以行内容区右边缘为基准，顺序为铂、金、银、铜。
        private static readonly float[] EconomyCoinIconRightOffsets = { 182f, 125f, 68f, 11f };
        private static readonly float[] EconomyCoinTextRightOffsets = { 194f, 137f, 80f, 23f };
        private static readonly int[] EconomyCoinItemTypes =
        {
            ItemID.PlatinumCoin,
            ItemID.GoldCoin,
            ItemID.SilverCoin,
            ItemID.CopperCoin
        };

        private static readonly Color DimmedGroupMetricColor = new Color(145, 154, 181);
        private static readonly Color DimmedItemMetricColor = new Color(112, 122, 151);

        private readonly UIText markerText;
        private readonly UIElement nameContainer;
        private readonly UIText nameText;
        private readonly UIText eventsText;
        private readonly UIText unitsText;
        private readonly UIText valueText;
        private readonly UIText[] economyCoinTexts = new UIText[4];
        private readonly long[] economyCoinValues = new long[4];
        private Action clickAction;
        private Asset<Texture2D> icon;
        private StatisticsTileIconLayout tileIconLayout;
        private int iconType;
        private StatisticsIconKind iconKind;
        private int depth;
        private bool useAlternateColor;
        private bool showEconomyCoins;
        private int economySpecialCurrencyItemType;
        private Asset<Texture2D> economySpecialCurrencyIcon;
        private float iconOffset = IconCenter;
        private Color economyCoinIconColor = Color.White;

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

            unitsText = new UIText(string.Empty, 0.76f);
            unitsText.Left.Set(-ItemActivityUseEventsRight, 0f);
            unitsText.HAlign = 1f;
            unitsText.VAlign = 0.5f;
            Append(unitsText);

            valueText = new UIText(string.Empty, 0.76f);
            valueText.Left.Set(-4f, 0f);
            valueText.HAlign = 1f;
            valueText.VAlign = 0.5f;
            Append(valueText);

            for (int index = 0; index < economyCoinTexts.Length; index++)
            {
                UIText coinText = new UIText(string.Empty, 0.76f);
                coinText.Left.Set(-EconomyCoinTextRightOffsets[index], 0f);
                coinText.HAlign = 1f;
                coinText.VAlign = 0.5f;
                economyCoinTexts[index] = coinText;
                Append(coinText);
            }
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
            int groupIconType,
            Asset<Texture2D> groupIcon,
            StatisticsIconKind groupIconKind,
            int amountIconType,
            Asset<Texture2D> amountIcon,
            StatisticsMetricDisplay metricDisplay,
            int depth,
            bool useAlternateColor,
            Action onClick)
        {
            // 可展开且带图标的分组依次显示箭头、图标和名称；不可展开分组直接复用
            // 普通内容行的图标槽位。
            this.depth = depth;
            this.useAlternateColor = useAlternateColor;
            clickAction = canExpand ? onClick : null;
            iconType = groupIconType;
            iconKind = groupIcon == null ? StatisticsIconKind.None : groupIconKind;
            markerText.Left.Set(MarkerLeft + depth * HierarchyIndent, 0f);
            markerText.SetText(canExpand ? (isExpanded ? "▼" : "▶") : string.Empty);
            markerText.TextColor = new Color(225, 232, 255);

            icon = groupIcon;
            tileIconLayout = null;
            bool reserveExpansionSlotForIcon = canExpand && icon != null;
            iconOffset = IconCenter
                + depth * HierarchyIndent
                + (reserveExpansionSlotForIcon ? GroupIconAfterMarkerOffset : 0f);

            SetNameIndent(depth, metricDisplay, reserveExpansionSlotForIcon);
            nameText.SetText(name);
            nameText.TextColor = new Color(238, 241, 255);
            SetMetrics(
                totalUnits,
                totalEvents,
                totalAmount,
                metricDisplay,
                amountIconType,
                amountIcon);
            SetMetricColors(
                totalUnits,
                totalEvents,
                totalAmount,
                metricDisplay,
                new Color(238, 241, 255),
                DimmedGroupMetricColor);
        }

        /// <summary>
        /// 将当前行绑定为指定层级的统计叶子项。
        /// </summary>
        internal void BindItem(
            StatisticsListItem item,
            StatisticsMetricDisplay metricDisplay,
            int depth,
            bool useAlternateColor)
        {
            // 子项恢复图标缩进，并清除行池上一次绑定的分组点击动作。
            this.depth = depth;
            this.useAlternateColor = useAlternateColor;
            clickAction = null;
            markerText.SetText(string.Empty);

            iconType = item.ItemType;
            iconKind = item.IconKind;
            icon = item.Icon;
            tileIconLayout = item.TileIconLayout;
            iconOffset = IconCenter + depth * HierarchyIndent;

            // 同一层级的分组与普通条目共用文字起点；箭头或图标只占左侧槽位。
            SetNameIndent(depth, metricDisplay);
            nameText.SetText(item.Name);
            nameText.TextColor = new Color(205, 214, 239);
            SetMetrics(
                item.Units,
                item.Events,
                item.Amount,
                metricDisplay,
                item.AmountIconType,
                item.AmountIcon);
            SetMetricColors(
                item.Units,
                item.Events,
                item.Amount,
                metricDisplay,
                new Color(190, 200, 225),
                DimmedItemMetricColor);
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
            unitsText.SetText(string.Empty);
            valueText.SetText(string.Empty);
            HideEconomyCoins();
            icon = null;
            tileIconLayout = null;
        }

        public override void Update(GameTime gameTime)
        {
            BackgroundColor = GetBackgroundColor(depth, useAlternateColor, IsMouseHovering);

            base.Update(gameTime);
        }

        /// <summary>
        /// 为不同树层级提供清晰区分，同时让同级相邻行以接近色交替显示。
        /// 所有色阶保持原有蓝紫色调，超过三级的内容沿用最深层颜色。
        /// </summary>
        private static Color GetBackgroundColor(
            int rowDepth,
            bool alternate,
            bool isHovered)
        {
            int paletteIndex = Math.Min(Math.Max(rowDepth, 0), 3);
            if (isHovered)
            {
                return (paletteIndex, alternate) switch
                {
                    (0, false) => new Color(82, 102, 184, 250),
                    (0, true) => new Color(90, 111, 197, 250),
                    (1, false) => new Color(59, 77, 143, 245),
                    (1, true) => new Color(65, 84, 153, 245),
                    (2, false) => new Color(44, 59, 111, 240),
                    (2, true) => new Color(49, 65, 121, 240),
                    (3, false) => new Color(35, 48, 92, 235),
                    _ => new Color(40, 54, 101, 235)
                };
            }

            return (paletteIndex, alternate) switch
            {
                (0, false) => new Color(57, 72, 135, 240),
                (0, true) => new Color(64, 80, 148, 240),
                (1, false) => new Color(40, 53, 101, 228),
                (1, true) => new Color(45, 59, 111, 228),
                (2, false) => new Color(28, 39, 76, 218),
                (2, true) => new Color(32, 44, 84, 218),
                (3, false) => new Color(22, 31, 62, 210),
                _ => new Color(26, 36, 70, 210)
            };
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

            if (icon != null)
            {
                DrawContentIcon(spriteBatch, dimensions);
            }

            if (showEconomyCoins)
            {
                DrawEconomyCoinIcons(spriteBatch, dimensions);
            }
        }

        /// <summary>
        /// 绘制叶子项或分组的物品、物块、墙、NPC、Buff 图标。
        /// </summary>
        private void DrawContentIcon(SpriteBatch spriteBatch, CalculatedStyle dimensions)
        {
            // 只在虚拟化后的可见行访问 Value，使尚未出现过的内容贴图按需完成加载。
            Texture2D texture = icon.Value;
            if (tileIconLayout != null)
            {
                DrawTileGridIcon(spriteBatch, dimensions, texture, tileIconLayout);
                return;
            }

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
        /// 分别绘制多格对象的每个图集分片，并在目标区域中去掉原图的分片边距。
        /// </summary>
        private void DrawTileGridIcon(
            SpriteBatch spriteBatch,
            CalculatedStyle dimensions,
            Texture2D texture,
            StatisticsTileIconLayout layout)
        {
            int rows = layout.CoordinateHeights?.Length ?? 0;
            if (layout.Columns <= 0
                || rows <= 0
                || layout.CoordinateWidth <= 0)
            {
                return;
            }

            int contentWidth = layout.Columns * layout.CoordinateWidth;
            int contentHeight = 0;
            foreach (int rowHeight in layout.CoordinateHeights)
            {
                contentHeight += Math.Max(rowHeight, 0);
            }

            if (contentWidth <= 0 || contentHeight <= 0)
            {
                return;
            }

            float scale = MathHelper.Min(24f / contentWidth, 24f / contentHeight);
            Vector2 iconCenter = new Vector2(
                dimensions.X + iconOffset,
                dimensions.Y + dimensions.Height * 0.5f);
            Vector2 topLeft = iconCenter
                - new Vector2(contentWidth, contentHeight) * scale * 0.5f;
            int sourceY = layout.SourceY;
            int destinationY = 0;
            for (int row = 0; row < rows; row++)
            {
                int rowHeight = layout.CoordinateHeights[row];
                for (int column = 0; column < layout.Columns; column++)
                {
                    Rectangle source = new Rectangle(
                        layout.SourceX
                            + column * (layout.CoordinateWidth + layout.CoordinatePadding),
                        sourceY,
                        layout.CoordinateWidth,
                        rowHeight);
                    if (!texture.Bounds.Contains(source))
                    {
                        continue;
                    }

                    Vector2 position = topLeft + new Vector2(
                        column * layout.CoordinateWidth,
                        destinationY) * scale;
                    spriteBatch.Draw(
                        texture,
                        position,
                        source,
                        Color.White,
                        0f,
                        Vector2.Zero,
                        scale,
                        SpriteEffects.None,
                        0f);
                }

                sourceY += rowHeight + layout.CoordinatePadding;
                destinationY += rowHeight;
            }
        }

        /// <summary>
        /// 在固定的四个币种槽位中绘制当前行可见的金币图标。
        /// </summary>
        private void DrawEconomyCoinIcons(SpriteBatch spriteBatch, CalculatedStyle dimensions)
        {
            if (economySpecialCurrencyIcon != null)
            {
                DrawEconomyCurrencyIcon(
                    spriteBatch,
                    dimensions,
                    economySpecialCurrencyIcon.Value,
                    economySpecialCurrencyItemType,
                    EconomyCoinIconRightOffsets[EconomyCoinIconRightOffsets.Length - 1]);
                return;
            }

            for (int index = 0; index < EconomyCoinItemTypes.Length; index++)
            {
                if (string.IsNullOrEmpty(economyCoinTexts[index].Text))
                {
                    continue;
                }

                int itemType = EconomyCoinItemTypes[index];
                Texture2D texture = TextureAssets.Item[itemType].Value;
                DrawEconomyCurrencyIcon(
                    spriteBatch,
                    dimensions,
                    texture,
                    itemType,
                    EconomyCoinIconRightOffsets[index]);
            }
        }

        private void DrawEconomyCurrencyIcon(
            SpriteBatch spriteBatch,
            CalculatedStyle dimensions,
            Texture2D texture,
            int itemType,
            float rightOffset)
        {
            Rectangle sourceRectangle = GetFirstIconFrame(
                texture,
                itemType,
                StatisticsIconKind.Item);
            float scale = MathHelper.Min(
                EconomyCoinIconSize / sourceRectangle.Width,
                EconomyCoinIconSize / sourceRectangle.Height);
            Vector2 iconCenter = new Vector2(
                dimensions.X + dimensions.Width - rightOffset,
                dimensions.Y + dimensions.Height * 0.5f);
            spriteBatch.Draw(
                texture,
                iconCenter,
                sourceRectangle,
                economyCoinIconColor,
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

            if (kind == StatisticsIconKind.Tile || kind == StatisticsIconKind.Wall)
            {
                // 物块与墙体图集都能用左上角 16 像素内容帧作为紧凑列表图标。
                return new Rectangle(
                    0,
                    0,
                    Math.Min(16, texture.Width),
                    Math.Min(16, texture.Height));
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
        private void SetNameIndent(
            int depth,
            StatisticsMetricDisplay metricDisplay,
            bool reserveExpansionSlotForIcon = false)
        {
            float left = NameLeft
                + depth * HierarchyIndent
                + (reserveExpansionSlotForIcon ? GroupNameAfterMarkerOffset : 0f);
            nameContainer.Left.Set(left, 0f);
            float rightReserve = metricDisplay switch
            {
                StatisticsMetricDisplay.Mixed => MixedNameRightReserve,
                StatisticsMetricDisplay.ItemActivity => ItemActivityNameRightReserve,
                StatisticsMetricDisplay.EconomyTransactions => ItemActivityNameRightReserve,
                _ => NameRightReserve
            };
            nameContainer.Width.Set(-left - rightReserve, 1f);
        }

        /// <summary>
        /// 按页面口径设置一至三个数值列。
        /// </summary>
        private void SetMetrics(
            long units,
            long events,
            long amount,
            StatisticsMetricDisplay metricDisplay,
            int amountIconType,
            Asset<Texture2D> amountIcon)
        {
            HideEconomyCoins();
            if (metricDisplay == StatisticsMetricDisplay.ItemActivity)
            {
                eventsText.Left.Set(-ItemActivityPickupUnitsRight, 0f);
                eventsText.SetText(units.ToString("N0"));
                unitsText.SetText(events.ToString("N0"));
                valueText.SetText(amount.ToString("N0"));
                return;
            }

            if (metricDisplay == StatisticsMetricDisplay.EconomyTransactions)
            {
                eventsText.Left.Set(-ItemActivityPickupUnitsRight, 0f);
                eventsText.SetText(units.ToString("N0"));
                unitsText.SetText(string.Empty);
                valueText.SetText(string.Empty);
                if (amountIconType != 0 || amountIcon != null)
                {
                    SetEconomySpecialCurrency(amount, amountIconType, amountIcon);
                }
                else
                {
                    SetEconomyCoins(amount);
                }
                return;
            }

            eventsText.Left.Set(-MixedEventsRight, 0f);
            unitsText.SetText(string.Empty);
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
        /// 对物品页、经济页和生存页的零值单元格分别使用弱化颜色。
        /// </summary>
        private void SetMetricColors(
            long units,
            long events,
            long amount,
            StatisticsMetricDisplay metricDisplay,
            Color normalColor,
            Color dimmedColor)
        {
            eventsText.TextColor = normalColor;
            unitsText.TextColor = normalColor;
            valueText.TextColor = normalColor;

            if (metricDisplay == StatisticsMetricDisplay.ItemActivity)
            {
                eventsText.TextColor = units == 0L ? dimmedColor : normalColor;
                unitsText.TextColor = events == 0L ? dimmedColor : normalColor;
                valueText.TextColor = amount == 0L ? dimmedColor : normalColor;
            }
            else if (metricDisplay == StatisticsMetricDisplay.EconomyTransactions)
            {
                eventsText.TextColor = units == 0L ? dimmedColor : normalColor;
                Color coinTextColor = amount == 0L ? dimmedColor : normalColor;
                foreach (UIText coinText in economyCoinTexts)
                {
                    coinText.TextColor = coinTextColor;
                }

                economyCoinIconColor = amount == 0L
                    ? new Color(dimmedColor.R, dimmedColor.G, dimmedColor.B, 180)
                    : Color.White;
            }
            else if (metricDisplay == StatisticsMetricDisplay.Mixed)
            {
                eventsText.TextColor = events == 0L ? dimmedColor : normalColor;
                valueText.TextColor = amount == 0L ? dimmedColor : normalColor;
            }
        }

        /// <summary>
        /// 将铜币总额拆分到固定币种槽位，并隐藏最左侧连续的零值高位币种。
        /// </summary>
        private void SetEconomyCoins(long copperAmount)
        {
            economySpecialCurrencyItemType = 0;
            economySpecialCurrencyIcon = null;
            long remaining = Math.Max(0L, copperAmount);
            economyCoinValues[0] = remaining / 1_000_000L;
            remaining %= 1_000_000L;
            economyCoinValues[1] = remaining / 10_000L;
            remaining %= 10_000L;
            economyCoinValues[2] = remaining / 100L;
            economyCoinValues[3] = remaining % 100L;

            int firstVisibleIndex = 0;
            while (firstVisibleIndex < economyCoinValues.Length - 1
                && economyCoinValues[firstVisibleIndex] == 0L)
            {
                firstVisibleIndex++;
            }

            for (int index = 0; index < economyCoinTexts.Length; index++)
            {
                economyCoinTexts[index].SetText(index < firstVisibleIndex
                    ? string.Empty
                    : economyCoinValues[index].ToString("N0"));
                Main.instance.LoadItem(EconomyCoinItemTypes[index]);
            }

            showEconomyCoins = true;
        }

        /// <summary>
        /// 在金额列最右侧以“数量 + 货币物品图标”显示一种特殊货币。
        /// </summary>
        private void SetEconomySpecialCurrency(
            long amount,
            int currencyItemType,
            Asset<Texture2D> currencyIcon)
        {
            for (int index = 0; index < economyCoinTexts.Length - 1; index++)
            {
                economyCoinTexts[index].SetText(string.Empty);
            }

            economyCoinTexts[economyCoinTexts.Length - 1]
                .SetText(Math.Max(0L, amount).ToString("N0"));
            economySpecialCurrencyItemType = currencyItemType;
            economySpecialCurrencyIcon = currencyIcon;
            showEconomyCoins = true;
        }

        /// <summary>
        /// 清除行池中上一次经济统计绑定留下的币种文字和图标状态。
        /// </summary>
        private void HideEconomyCoins()
        {
            showEconomyCoins = false;
            economySpecialCurrencyItemType = 0;
            economySpecialCurrencyIcon = null;
            foreach (UIText coinText in economyCoinTexts)
            {
                coinText.SetText(string.Empty);
            }
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

            if (metricDisplay == StatisticsMetricDisplay.UnitsAndEventsHideEqual
                && units == events)
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
