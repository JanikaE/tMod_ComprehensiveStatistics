using ComprehensiveStatistics.Common.Players;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 在新物品覆盖垃圾槽旧内容时记录真正不可恢复的物品，排除放入后又取回的情况。
    /// </summary>
    internal sealed class StatisticsItemDiscardSystem : ModSystem
    {
        public override void Load()
        {
            On_ItemSlot.SellOrTrash += HandleSellOrTrash;
            On_ItemSlot.LeftClick_ItemArray_int_int += HandleLeftClick;
        }

        public override void Unload()
        {
            On_ItemSlot.SellOrTrash -= HandleSellOrTrash;
            On_ItemSlot.LeftClick_ItemArray_int_int -= HandleLeftClick;
        }

        /// <summary>
        /// 处理鼠标物品直接覆盖垃圾槽的路径；堆叠、交换和取回均不视为彻底丢弃。
        /// </summary>
        private static void HandleLeftClick(
            On_ItemSlot.orig_LeftClick_ItemArray_int_int orig,
            Item[] inventory,
            int context,
            int slot)
        {
            bool hasValidSlot = inventory != null
                && slot >= 0
                && slot < inventory.Length
                && inventory[slot] != null;
            Item previousTrashItem = hasValidSlot ? inventory[slot].Clone() : null;
            Item incomingItem = Main.mouseItem?.Clone();
            int movementAction = context == ItemSlot.Context.TrashItem
                && previousTrashItem?.IsAir == false
                && incomingItem?.IsAir == false
                    ? ItemSlot.PickItemMovementAction(
                        inventory,
                        context,
                        slot,
                        Main.mouseItem)
                    : -1;

            orig(inventory, context, slot);

            if (movementAction != 0
                || Main.mouseItem == null
                || !Main.mouseItem.IsAir
                || inventory[slot] == null
                || inventory[slot].IsAir
                || inventory[slot].IsNotSameTypePrefixAndStack(incomingItem))
            {
                return;
            }

            // TrashItem 上的移动动作 0 会先清空旧槽位，再放入鼠标物品；旧内容不会
            // 像普通槽位交换那样回到鼠标。其他动作（含同类堆叠）不在这里结算。
            Main.LocalPlayer
                .GetModPlayer<StatisticsPlayer>()
                .RecordItemDiscard(previousTrashItem);
        }

        /// <summary>
        /// 包装原版快速出售或丢弃入口，仅在非商店状态下确认旧垃圾槽内容被覆盖后结算。
        /// </summary>
        private static void HandleSellOrTrash(
            On_ItemSlot.orig_SellOrTrash orig,
            Item[] inventory,
            int context,
            int slot)
        {
            Player player = Main.LocalPlayer;
            bool hasValidSource = inventory != null
                && slot >= 0
                && slot < inventory.Length
                && inventory[slot] != null
                && !inventory[slot].IsAir;
            Item incomingItem = hasValidSource ? inventory[slot].Clone() : null;
            Item previousTrashReference = player?.trashItem;
            Item previousTrashItem = player?.trashItem?.Clone();
            bool shopWasOpen = Main.npcShop > 0;

            orig(inventory, context, slot);

            if (shopWasOpen
                || player == null
                || incomingItem == null
                || incomingItem.favorited
                || previousTrashItem == null
                || previousTrashItem.IsAir
                || inventory[slot] == null
                || !inventory[slot].IsAir
                || player.trashItem == null
                || player.trashItem.IsAir
                || ReferenceEquals(player.trashItem, previousTrashReference)
                || player.trashItem.IsNotSameTypePrefixAndStack(incomingItem))
            {
                return;
            }

            // 原版只有快速丢弃会在非商店状态下清空来源槽并以其完整克隆覆盖垃圾槽。
            // 此时旧内容已经没有任何取回路径；直接点击垃圾槽的交换/取回不会进入本方法。
            player.GetModPlayer<StatisticsPlayer>().RecordItemDiscard(previousTrashItem);
        }
    }
}
