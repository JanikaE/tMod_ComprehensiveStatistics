using ComprehensiveStatistics.Common.Players;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 观察原版标准鱼饵选择与消耗入口，将明确的消耗结果交给已建立的抛竿会话。
    /// </summary>
    internal sealed class StatisticsBaitSystem : ModSystem
    {
        private const int FirstAmmoSlot = 54;
        private const int InventoryAndAmmoSlotCount = 58;
        private const int MainInventorySlotCount = 50;

        public override void Load()
        {
            On_Player.ItemCheck_CheckFishingBobber_PickAndConsumeBait
                += HandlePickAndConsumeBait;
        }

        public override void Unload()
        {
            On_Player.ItemCheck_CheckFishingBobber_PickAndConsumeBait
                -= HandlePickAndConsumeBait;
        }

        /// <summary>
        /// 在原版唯一的“选择并尝试消耗鱼饵”路径前后比较库存，
        /// 同时使用输出的鱼饵类型确认本次实际选中的具体物品。
        /// </summary>
        private static void HandlePickAndConsumeBait(
            On_Player.orig_ItemCheck_CheckFishingBobber_PickAndConsumeBait orig,
            Player player,
            Projectile bobber,
            out bool caught,
            out int baitType)
        {
            Item selectedBait = null;
            bool shouldObserve = IsOwnedLocalFishingPlayer(player)
                && TryGetSelectedBait(player, out selectedBait);
            int selectedType = shouldObserve ? selectedBait.type : ItemID.None;
            long inventoryUnitsBefore = shouldObserve
                ? CountInventoryUnits(player, selectedType)
                : 0L;

            orig(player, bobber, out caught, out baitType);

            if (!shouldObserve
                || !caught
                || baitType != selectedType
                || selectedType <= ItemID.None)
            {
                return;
            }

            long actualConsumption = System.Math.Max(
                0L,
                inventoryUnitsBefore - CountInventoryUnits(player, selectedType));
            player.GetModPlayer<StatisticsPlayer>().ObserveFishingBaitDecision(
                bobber,
                selectedType,
                expectedConsumption: 1L,
                actualConsumption);
        }

        /// <summary>
        /// 按原版顺序取得当前鱼饵：先从四个弹药槽自上而下查找，
        /// 再从主背包左上到右下查找。
        /// </summary>
        internal static bool TryGetSelectedBait(Player player, out Item bait)
        {
            bait = null;
            if (player?.inventory == null)
            {
                return false;
            }

            int slotCount = System.Math.Min(
                InventoryAndAmmoSlotCount,
                player.inventory.Length);
            for (int slot = FirstAmmoSlot; slot < slotCount; slot++)
            {
                if (IsUsableBait(player.inventory[slot]))
                {
                    bait = player.inventory[slot];
                    return true;
                }
            }

            int mainInventoryCount = System.Math.Min(
                MainInventorySlotCount,
                player.inventory.Length);
            for (int slot = 0; slot < mainInventoryCount; slot++)
            {
                if (IsUsableBait(player.inventory[slot]))
                {
                    bait = player.inventory[slot];
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsableBait(Item item)
        {
            return item != null
                && !item.IsAir
                && item.type > ItemID.None
                && item.stack > 0
                && item.bait > 0;
        }

        private static bool IsOwnedLocalFishingPlayer(Player player)
        {
            return !Main.dedServ
                && !Main.gameMenu
                && player?.active == true
                && player.whoAmI == Main.myPlayer;
        }

        private static long CountInventoryUnits(Player player, int itemType)
        {
            long total = 0L;
            if (player?.inventory == null || itemType <= ItemID.None)
            {
                return total;
            }

            int slotCount = System.Math.Min(
                InventoryAndAmmoSlotCount,
                player.inventory.Length);
            for (int slot = 0; slot < slotCount; slot++)
            {
                Item item = player.inventory[slot];
                if (item?.type == itemType && item.stack > 0)
                {
                    total += item.stack;
                }
            }

            return total;
        }
    }
}
