using System;
using ComprehensiveStatistics.Common.Players;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 观察税收官领取按钮的完整执行结果，并记录实际生成的钱币价值。
    /// </summary>
    internal sealed class StatisticsTaxCollectionSystem : ModSystem
    {
        public override void Load()
        {
            On_Main.GUIChatDrawInner += HandleNpcChat;
        }

        public override void Unload()
        {
            On_Main.GUIChatDrawInner -= HandleNpcChat;
        }

        /// <summary>
        /// 只有税款从正数变为零时才表示本帧完成领取；金额沿用原版价格调整公式。
        /// </summary>
        private static void HandleNpcChat(
            On_Main.orig_GUIChatDrawInner orig,
            Main self)
        {
            Player player = Main.LocalPlayer;
            bool isTaxCollectorConversation = player != null
                && player.whoAmI == Main.myPlayer
                && player.talkNPC >= 0
                && player.talkNPC < Main.npc.Length
                && Main.npc[player.talkNPC]?.active == true
                && Main.npc[player.talkNPC].type == NPCID.TaxCollector;
            int taxMoneyBefore = isTaxCollectorConversation
                ? Math.Max(0, player.taxMoney)
                : 0;
            double priceAdjustment = isTaxCollectorConversation
                ? player.currentShoppingSettings.PriceAdjustment
                : 1d;

            orig(self);

            if (isTaxCollectorConversation
                && taxMoneyBefore > 0
                && player.taxMoney == 0)
            {
                long collectedCoins = priceAdjustment > 0d
                    ? Math.Max(0L, (long)(taxMoneyBefore / priceAdjustment))
                    : 0L;
                player.GetModPlayer<StatisticsPlayer>()
                    .RecordTaxCollection(collectedCoins);
            }
        }
    }
}
