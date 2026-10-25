using ComprehensiveStatistics.Common.Configs;
using ComprehensiveStatistics.Common.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.UI;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.UI
{
    /// <summary>
    /// 物品栏旁的统计入口，支持右键拖动并把相对位置保存到客户端配置。
    /// </summary>
    internal sealed class InventoryStatisticsButtonState : UIState
    {
        private const float ButtonSize = 34f;

        private InventoryIconButton buttonPanel;
        private bool isDragging;
        private Vector2 dragOffset;

        public override void OnInitialize()
        {
            Asset<Texture2D> iconTexture = ModContent
                .GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>()
                .SmallModIcon;
            Asset<Texture2D> hoverOutlineTexture = ModContent.Request<Texture2D>(
                "ComprehensiveStatistics/Assets/UI/InventoryStatisticsButtonOutline");
            buttonPanel = new InventoryIconButton(iconTexture, hoverOutlineTexture);
            buttonPanel.Width.Set(ButtonSize, 0f);
            buttonPanel.Height.Set(ButtonSize, 0f);
            buttonPanel.OnLeftClick += OpenStatistics;

            Append(buttonPanel);
        }

        public override void Update(GameTime gameTime)
        {
            if (isDragging)
            {
                MoveToMouse();
            }
            else
            {
                PositionFromConfig();
            }

            if (buttonPanel.IsMouseHovering || isDragging)
            {
                Main.LocalPlayer.mouseInterface = true;
            }

            base.Update(gameTime);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);

            if (buttonPanel.IsMouseHovering && !isDragging)
            {
                UICommon.TooltipMouseText(Language.GetTextValue("Mods.ComprehensiveStatistics.UI.OpenButtonTooltip"));
            }
        }

        public override void RightMouseDown(UIMouseEvent evt)
        {
            base.RightMouseDown(evt);

            if (!buttonPanel.ContainsPoint(evt.MousePosition))
            {
                return;
            }

            CalculatedStyle dimensions = buttonPanel.GetDimensions();
            // 记录鼠标相对按钮左上角的偏移，拖动时不会发生跳变。
            dragOffset = evt.MousePosition - new Vector2(dimensions.X, dimensions.Y);
            isDragging = true;
            Main.LocalPlayer.mouseInterface = true;
        }

        public override void RightMouseUp(UIMouseEvent evt)
        {
            base.RightMouseUp(evt);

            if (!isDragging)
            {
                return;
            }

            MoveToMouse();
            isDragging = false;
            SavePosition();
        }

        /// <summary>
        /// 将归一化配置坐标换算为当前 UI 画布中的按钮位置。
        /// </summary>
        private void PositionFromConfig()
        {
            GetMovementBounds(out float maxLeft, out float maxTop);
            ClientConfig config = ClientConfig.Instance;
            float left = maxLeft * (config?.InventoryButtonHorizontal ?? 0.95f);
            float top = maxTop * (config?.InventoryButtonVertical ?? 0.09f);

            SetPosition(left, top);
        }

        /// <summary>
        /// 按拖动偏移移动按钮，并将其限制在可见 UI 画布内。
        /// </summary>
        private void MoveToMouse()
        {
            GetMovementBounds(out float maxLeft, out float maxTop);
            float left = MathHelper.Clamp(Main.MouseScreen.X - dragOffset.X, 0f, maxLeft);
            float top = MathHelper.Clamp(Main.MouseScreen.Y - dragOffset.Y, 0f, maxTop);

            SetPosition(left, top);
            Main.LocalPlayer.mouseInterface = true;
        }

        /// <summary>
        /// 将按钮当前位置归一化后保存到本地客户端配置。
        /// </summary>
        private void SavePosition()
        {
            ClientConfig config = ClientConfig.Instance;
            if (config == null)
            {
                return;
            }

            GetMovementBounds(out float maxLeft, out float maxTop);
            // 保存 0～1 的比例而非像素，兼容后续分辨率和 UI 缩放变化。
            config.InventoryButtonHorizontal = maxLeft > 0f ? buttonPanel.Left.Pixels / maxLeft : 0f;
            config.InventoryButtonVertical = maxTop > 0f ? buttonPanel.Top.Pixels / maxTop : 0f;
            config.SaveChanges(broadcast: false);
        }

        /// <summary>
        /// 计算按钮左上角在当前 UI 缩放下允许移动的最大坐标。
        /// </summary>
        private static void GetMovementBounds(out float maxLeft, out float maxTop)
        {
            maxLeft = MathHelper.Max(0f, Main.screenWidth / Main.UIScale - ButtonSize);
            maxTop = MathHelper.Max(0f, Main.screenHeight / Main.UIScale - ButtonSize);
        }

        /// <summary>
        /// 仅在坐标变化时更新按钮位置并重新计算布局。
        /// </summary>
        private void SetPosition(float left, float top)
        {
            if (buttonPanel.Left.Pixels != left || buttonPanel.Top.Pixels != top)
            {
                buttonPanel.Left.Set(left, 0f);
                buttonPanel.Top.Set(top, 0f);
                Recalculate();
            }
        }

        /// <summary>
        /// 将入口按钮点击转交给统一统计 UI 系统。
        /// </summary>
        private static void OpenStatistics(UIMouseEvent evt, UIElement listeningElement)
        {
            ModContent.GetInstance<StatisticsUISystem>().Open();
        }

        /// <summary>
        /// 直接绘制模组小图标，并在悬停时沿贴图轮廓添加黄色像素描边。
        /// </summary>
        private sealed class InventoryIconButton : UIElement
        {
            private readonly Asset<Texture2D> texture;
            private readonly Asset<Texture2D> hoverOutlineTexture;

            /// <summary>
            /// 创建不带面板底图的物品栏统计入口。
            /// </summary>
            public InventoryIconButton(
                Asset<Texture2D> texture,
                Asset<Texture2D> hoverOutlineTexture)
            {
                this.texture = texture;
                this.hoverOutlineTexture = hoverOutlineTexture;
            }

            protected override void DrawSelf(SpriteBatch spriteBatch)
            {
                Texture2D icon = texture.Value;
                CalculatedStyle dimensions = GetDimensions();
                Vector2 drawPosition = new Vector2(
                    dimensions.X + (dimensions.Width - icon.Width) * 0.5f,
                    dimensions.Y + (dimensions.Height - icon.Height) * 0.5f);

                if (IsMouseHovering)
                {
                    spriteBatch.Draw(
                        hoverOutlineTexture.Value,
                        drawPosition - Vector2.One,
                        Color.White);
                }

                spriteBatch.Draw(icon, drawPosition, Color.White);
            }
        }
    }
}
