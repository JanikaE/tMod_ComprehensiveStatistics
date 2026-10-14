using ComprehensiveStatistics.Common.Configs;
using ComprehensiveStatistics.Common.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
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
        private const float ButtonSize = 44f;

        private UIPanel buttonPanel;
        private bool isDragging;
        private Vector2 dragOffset;

        public override void OnInitialize()
        {
            buttonPanel = new UIPanel();
            buttonPanel.Width.Set(ButtonSize, 0f);
            buttonPanel.Height.Set(ButtonSize, 0f);
            buttonPanel.SetPadding(5f);
            buttonPanel.BackgroundColor = new Color(63, 82, 151, 220);
            buttonPanel.BorderColor = new Color(89, 116, 213, 255);
            buttonPanel.OnLeftClick += OpenStatistics;

            Asset<Texture2D> iconTexture = ModContent.Request<Texture2D>($"Terraria/Images/Item_{ItemID.TallyCounter}");
            UIImage icon = new UIImage(iconTexture);
            icon.Width.Set(32f, 0f);
            icon.Height.Set(32f, 0f);
            icon.HAlign = 0.5f;
            icon.VAlign = 0.5f;
            buttonPanel.Append(icon);

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
                buttonPanel.BackgroundColor = new Color(83, 108, 198, 235);
            }
            else
            {
                buttonPanel.BackgroundColor = new Color(63, 82, 151, 220);
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
    }
}
