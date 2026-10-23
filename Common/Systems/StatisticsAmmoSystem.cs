using ComprehensiveStatistics.Common.Players;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 观察标准弹药选择与消耗流程，按实际选中的具体弹药记录应耗和库存实际消耗。
    /// </summary>
    internal sealed class StatisticsAmmoSystem : ModSystem
    {
        private const int StandardInventoryAndAmmoSlotCount = 58;

        private static AmmoPickObservation activeObservation;

        public override void Load()
        {
            On_Player.PickAmmo_Item_refInt32_refSingle_refBoolean_refInt32_refSingle_refInt32_bool
                += HandlePickAmmo;
            On_Player.ChooseAmmo += HandleChooseAmmo;
        }

        public override void Unload()
        {
            On_Player.PickAmmo_Item_refInt32_refSingle_refBoolean_refInt32_refSingle_refInt32_bool
                -= HandlePickAmmo;
            On_Player.ChooseAmmo -= HandleChooseAmmo;
            activeObservation = null;
        }

        /// <summary>
        /// 将一次实际供弹调用与内部选中的弹药实例关联，并在调用结束后计算库存净减少量。
        /// `dontConsume` 路径用于持有弹幕初始化或预览，真正射击会另行供弹，因此不计数。
        /// </summary>
        private static void HandlePickAmmo(
            On_Player.orig_PickAmmo_Item_refInt32_refSingle_refBoolean_refInt32_refSingle_refInt32_bool orig,
            Player player,
            Item weapon,
            ref int projectileType,
            ref float speed,
            ref bool canShoot,
            ref int damage,
            ref float knockBack,
            out int usedAmmoItemId,
            bool dontConsume)
        {
            AmmoPickObservation previousObservation = activeObservation;
            AmmoPickObservation observation = IsOwnedLocalAmmoUse(player, weapon, dontConsume)
                ? new AmmoPickObservation(player)
                : null;
            activeObservation = observation;

            try
            {
                orig(
                    player,
                    weapon,
                    ref projectileType,
                    ref speed,
                    ref canShoot,
                    ref damage,
                    ref knockBack,
                    out usedAmmoItemId,
                    dontConsume);
            }
            finally
            {
                activeObservation = previousObservation;
            }

            if (!canShoot
                || observation?.HasSelectedAmmo != true
                || observation.AmmoItemType != usedAmmoItemId)
            {
                return;
            }

            long actualConsumption = observation.GetActualConsumption();
            // 每次有效选中都对应一个应耗单位。无尽箭袋等非消耗型弹药
            // 同样累加应耗，但实耗始终为零，UI 通过物品属性将其消耗率显示为“—”。
            long expectedConsumption = System.Math.Max(1L, actualConsumption);
            player.GetModPlayer<StatisticsPlayer>().RecordAmmoUsage(
                observation.AmmoItemType,
                expectedConsumption,
                actualConsumption);
        }

        /// <summary>
        /// 捕获 PickAmmo 内部唯一实际选中的弹药实例，不重复调用选择回调。
        /// </summary>
        private static Item HandleChooseAmmo(
            On_Player.orig_ChooseAmmo orig,
            Player player,
            Item weapon)
        {
            Item ammo = orig(player, weapon);
            AmmoPickObservation observation = activeObservation;
            if (observation != null && ReferenceEquals(observation.Player, player))
            {
                observation.ObserveSelectedAmmo(ammo);
            }

            return ammo;
        }

        /// <summary>
        /// 判断调用是否属于当前客户端角色的一次可统计标准供弹。
        /// </summary>
        private static bool IsOwnedLocalAmmoUse(
            Player player,
            Item weapon,
            bool dontConsume)
        {
            return !dontConsume
                && !Main.dedServ
                && !Main.gameMenu
                && player?.active == true
                && player.whoAmI == Main.myPlayer
                && weapon != null
                && !weapon.IsAir
                && weapon.useAmmo > 0;
        }

        /// <summary>
        /// 保存一次供弹调用中选中的实例及其调用前库存，用于在调用后确认真实消耗量。
        /// </summary>
        private sealed class AmmoPickObservation
        {
            private Item selectedAmmo;
            private int selectedStackBefore;
            private long inventoryUnitsBefore;
            private bool selectedFromInventory;

            /// <summary>
            /// 创建一次属于指定本地玩家的供弹观察。
            /// </summary>
            internal AmmoPickObservation(Player player)
            {
                Player = player;
            }

            internal Player Player { get; }

            internal bool HasSelectedAmmo => selectedAmmo != null;

            internal int AmmoItemType { get; private set; }

            internal bool IsConsumable { get; private set; }

            /// <summary>
            /// 保存原版或模组选择回调返回的具体弹药；同一次供弹中的重复查询只采用首次有效结果。
            /// </summary>
            internal void ObserveSelectedAmmo(Item ammo)
            {
                if (selectedAmmo != null
                    || ammo == null
                    || ammo.IsAir
                    || ammo.ammo <= AmmoID.None
                    || ammo.type <= ItemID.None)
                {
                    return;
                }

                selectedAmmo = ammo;
                AmmoItemType = ammo.type;
                IsConsumable = ammo.consumable;
                selectedStackBefore = System.Math.Max(0, ammo.stack);
                inventoryUnitsBefore = CountInventoryUnits(Player, AmmoItemType);
                selectedFromInventory = IsInventoryReference(Player, ammo);
            }

            /// <summary>
            /// 使用同类型库存总量差确认实际消耗；非背包虚拟弹药则退回到选中实例的堆叠差。
            /// </summary>
            internal long GetActualConsumption()
            {
                if (!HasSelectedAmmo || !IsConsumable)
                {
                    return 0L;
                }

                if (selectedFromInventory)
                {
                    return System.Math.Max(
                        0L,
                        inventoryUnitsBefore - CountInventoryUnits(Player, AmmoItemType));
                }

                int remainingStack = selectedAmmo.type == AmmoItemType
                    ? System.Math.Max(0, selectedAmmo.stack)
                    : 0;
                return System.Math.Max(0, selectedStackBefore - remainingStack);
            }

            /// <summary>
            /// 累加角色背包和四个弹药槽中指定物品的当前堆叠数量。
            /// </summary>
            private static long CountInventoryUnits(Player player, int itemType)
            {
                long total = 0L;
                if (player?.inventory == null)
                {
                    return total;
                }

                int slotCount = System.Math.Min(
                    StandardInventoryAndAmmoSlotCount,
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

            /// <summary>
            /// 判断选择回调返回的实例是否正是角色背包中的某个槽位对象。
            /// </summary>
            private static bool IsInventoryReference(Player player, Item selected)
            {
                if (player?.inventory == null)
                {
                    return false;
                }

                int slotCount = System.Math.Min(
                    StandardInventoryAndAmmoSlotCount,
                    player.inventory.Length);
                for (int slot = 0; slot < slotCount; slot++)
                {
                    if (ReferenceEquals(player.inventory[slot], selected))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
