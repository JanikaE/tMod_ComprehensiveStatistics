using ComprehensiveStatistics.Common.Players;
using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 从原版货币扣款入口捕获重铸实际费用，供成功后的物品回调提交统计。
    /// </summary>
    internal sealed class StatisticsReforgeSystem : ModSystem
    {
        public override void Load()
        {
            On_Player.BuyItem += HandleBuyItem;
        }

        public override void Unload()
        {
            On_Player.BuyItem -= HandleBuyItem;
        }

        /// <summary>
        /// 在真实重铸完成扣款后保存精确价格；普通商店和特殊货币购买不进入此路径。
        /// </summary>
        private static bool HandleBuyItem(
            On_Player.orig_BuyItem orig,
            Player player,
            long price,
            int customCurrency)
        {
            Item reforgeItem = AutoRerollCompatibilitySystem.ActiveItem ?? Main.reforgeItem;
            bool isCoinReforge = customCurrency == -1
                && (AutoRerollCompatibilitySystem.ActiveItem != null || Main.InReforgeMenu)
                && player != null
                && player.whoAmI == Main.myPlayer
                && reforgeItem != null
                && !reforgeItem.IsAir;
            bool paid = orig(player, price, customCurrency);
            if (paid && isCoinReforge)
            {
                player.GetModPlayer<StatisticsPlayer>()
                    .ObserveReforgePayment(reforgeItem, price);
            }

            return paid;
        }
    }
}
