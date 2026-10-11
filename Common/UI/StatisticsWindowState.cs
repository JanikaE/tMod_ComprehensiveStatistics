using ComprehensiveStatistics.Common.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.UI
{
    internal sealed class StatisticsWindowState : UIState
    {
        private UIImageButton closeButton;

        public override void OnInitialize()
        {
            UIPanel window = new UIPanel();
            window.Width.Set(-80f, 0.75f);
            window.Height.Set(-80f, 0.72f);
            window.MinWidth.Set(420f, 0f);
            window.MinHeight.Set(280f, 0f);
            window.MaxWidth.Set(820f, 0f);
            window.MaxHeight.Set(600f, 0f);
            window.HAlign = 0.5f;
            window.VAlign = 0.5f;
            window.SetPadding(16f);
            window.BackgroundColor = new Color(33, 43, 79, 245);
            window.BorderColor = new Color(89, 116, 213, 255);
            Append(window);

            UIText title = new UIText(Language.GetText("Mods.ComprehensiveStatistics.UI.WindowTitle"), 1.15f);
            title.Left.Set(4f, 0f);
            title.Top.Set(4f, 0f);
            window.Append(title);

            UIText placeholder = new UIText(Language.GetText("Mods.ComprehensiveStatistics.UI.EmptyWindow"), 0.95f);
            placeholder.HAlign = 0.5f;
            placeholder.VAlign = 0.5f;
            placeholder.TextColor = new Color(190, 200, 225);
            window.Append(placeholder);

            Asset<Texture2D> closeTexture = ModContent.Request<Texture2D>("Terraria/Images/UI/ButtonDelete");
            closeButton = new UIImageButton(closeTexture);
            closeButton.Width.Set(22f, 0f);
            closeButton.Height.Set(22f, 0f);
            closeButton.Left.Set(-26f, 1f);
            closeButton.Top.Set(0f, 0f);
            closeButton.OnLeftClick += CloseWindow;
            window.Append(closeButton);
        }

        public override void Update(GameTime gameTime)
        {
            // The statistics window is modal. Prevent clicks on it (or its backdrop)
            // from using the held item or interacting with the world.
            Main.LocalPlayer.mouseInterface = true;

            if (closeButton.IsMouseHovering)
            {
                Main.hoverItemName = Language.GetTextValue("Mods.ComprehensiveStatistics.UI.CloseButtonTooltip");
            }

            base.Update(gameTime);
        }

        private static void CloseWindow(UIMouseEvent evt, UIElement listeningElement)
        {
            ModContent.GetInstance<StatisticsUISystem>().Close();
        }
    }
}
