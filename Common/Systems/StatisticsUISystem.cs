using System.Collections.Generic;
using ComprehensiveStatistics.Common.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.Systems
{
    [Autoload(Side = ModSide.Client)]
    internal sealed class StatisticsUISystem : ModSystem
    {
        private UserInterface inventoryButtonInterface;
        private UserInterface statisticsWindowInterface;
        private InventoryStatisticsButtonState inventoryButtonState;
        private StatisticsWindowState statisticsWindowState;

        internal bool IsOpen => statisticsWindowInterface?.CurrentState != null;

        public override void Load()
        {
            inventoryButtonState = new InventoryStatisticsButtonState();
            inventoryButtonState.Activate();
            inventoryButtonInterface = new UserInterface();
            inventoryButtonInterface.SetState(inventoryButtonState);

            statisticsWindowState = new StatisticsWindowState();
            statisticsWindowState.Activate();
            statisticsWindowInterface = new UserInterface();
        }

        public override void Unload()
        {
            inventoryButtonInterface = null;
            statisticsWindowInterface = null;
            inventoryButtonState = null;
            statisticsWindowState = null;
        }

        public override void OnWorldUnload()
        {
            Close(playSound: false);
        }

        internal void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        internal void Open()
        {
            if (IsOpen)
            {
                return;
            }

            Main.playerInventory = false;
            statisticsWindowInterface.SetState(statisticsWindowState);
            SoundEngine.PlaySound(SoundID.MenuOpen);
        }

        internal void Close(bool playSound = true)
        {
            if (!IsOpen)
            {
                return;
            }

            statisticsWindowInterface.SetState(null);

            if (playSound)
            {
                SoundEngine.PlaySound(SoundID.MenuClose);
            }
        }

        public override void UpdateUI(GameTime gameTime)
        {
            if (IsOpen)
            {
                // Escape is handled here so it closes this modal instead of opening
                // the vanilla inventory on the same frame.
                if (Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape))
                {
                    Close();
                    Main.playerInventory = false;
                    return;
                }

                Main.playerInventory = false;
                statisticsWindowInterface.Update(gameTime);
                return;
            }

            if (Main.playerInventory)
            {
                inventoryButtonInterface.Update(gameTime);
            }
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int mouseTextIndex = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
            if (mouseTextIndex < 0)
            {
                return;
            }

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "ComprehensiveStatistics: Statistics UI",
                delegate
                {
                    if (IsOpen)
                    {
                        statisticsWindowInterface.Draw(Main.spriteBatch, new GameTime());
                    }
                    else if (Main.playerInventory)
                    {
                        inventoryButtonInterface.Draw(Main.spriteBatch, new GameTime());
                    }

                    return true;
                },
                InterfaceScaleType.UI));
        }
    }
}
