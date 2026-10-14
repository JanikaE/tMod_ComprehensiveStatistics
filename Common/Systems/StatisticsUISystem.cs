using System.Collections.Generic;
using ComprehensiveStatistics.Common.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 统一管理物品栏入口和统计窗口的生命周期、输入与绘制层。
    /// </summary>
    [Autoload(Side = ModSide.Client)]
    internal sealed class StatisticsUISystem : ModSystem
    {
        private UserInterface inventoryButtonInterface;
        private UserInterface statisticsWindowInterface;
        private InventoryStatisticsButtonState inventoryButtonState;
        private StatisticsWindowState statisticsWindowState;
        private int selectedItemBeforeOpen;

        internal bool IsOpen => statisticsWindowInterface?.CurrentState != null;

        internal bool IsTakingTextInput => IsOpen && statisticsWindowState.IsWritingSearchText;

        public override void Load()
        {
            // 两套 UserInterface 分开管理，避免入口按钮与模态窗口互相影响状态。
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

        /// <summary>
        /// 在打开和关闭状态之间切换统计窗口。
        /// </summary>
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

        /// <summary>
        /// 打开统计窗口并保存当前快捷栏选择。
        /// </summary>
        internal void Open()
        {
            if (IsOpen)
            {
                return;
            }

            Main.playerInventory = false;
            selectedItemBeforeOpen = Main.LocalPlayer.selectedItem;
            statisticsWindowInterface.SetState(statisticsWindowState);
            SoundEngine.PlaySound(SoundID.MenuOpen);
        }

        /// <summary>
        /// 关闭统计窗口，并按需播放关闭音效。
        /// </summary>
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
                // 在这里接管 Esc，避免关闭模态窗口的同一帧又打开原版物品栏。
                if (Main.keyState.IsKeyDown(Keys.Escape)
                    && Main.oldKeyState.IsKeyUp(Keys.Escape)
                    && !statisticsWindowState.IsWritingSearchText)
                {
                    Close();
                    Main.playerInventory = false;
                    return;
                }

                Main.playerInventory = false;
                statisticsWindowInterface.Update(gameTime);

                // 统计列表已经消费滚轮输入，不能再让同一输入切换原版快捷栏。
                Main.LocalPlayer.selectedItem = selectedItemBeforeOpen;
                PlayerInput.ScrollWheelDeltaForUI = 0;
                PlayerInput.ScrollWheelDelta = 0;
                return;
            }

            if (Main.playerInventory)
            {
                inventoryButtonInterface.Update(gameTime);
            }
        }

        public override void PostUpdateInput()
        {
            if (!IsOpen || Main.gameMenu)
            {
                return;
            }

            // 此回调位于输入轮询之后、游戏逻辑消费之前。保留 UI 专用滚轮值供列表
            // 使用，只清除游戏滚轮值，并恢复打开窗口前的快捷栏槽位作为双重保护。
            PlayerInput.ScrollWheelDelta = 0;
            Main.LocalPlayer.selectedItem = selectedItemBeforeOpen;
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            // 插在鼠标提示层之前，使窗口覆盖世界界面，同时仍允许提示文本显示在最上方。
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
