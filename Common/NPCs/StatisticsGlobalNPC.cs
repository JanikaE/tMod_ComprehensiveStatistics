using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Systems;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.NPCs
{
    /// <summary>
    /// 采集 NPC 对话按钮、角色战斗归属与世界死亡，并阻止多部位重复结算。
    /// </summary>
    internal sealed class StatisticsGlobalNPC : GlobalNPC
    {
        private static ulong lastEaterOfWorldsDeathTick = ulong.MaxValue;

        private bool deathRecorded;

        public override bool InstancePerEntity => true;

        public override void OnChatButtonClicked(NPC npc, bool firstButton)
        {
            // GlobalNPC 对话回调只应发生在本地客户端；这里仍保留权威端检查，
            // 防止未来生命周期变化或其他模组手动调用时污染远程玩家数据。
            if (Main.netMode == NetmodeID.Server
                || Main.LocalPlayer?.active != true
                || npc == null)
            {
                return;
            }

            Main.LocalPlayer.GetModPlayer<StatisticsPlayer>().RecordNpcChatButton(
                npc,
                NpcChatButtonClassifier.Classify(npc, firstButton));
        }

        public override void OnKill(NPC npc)
        {
            if (!NpcDeathNormalizer.TryResolveDeathTarget(
                    npc,
                    out NPC canonicalNpc,
                    out StableContentKey content,
                    out int lastInteraction,
                    out bool usesEaterOfWorldsTickDedupe)
                || !TryBeginDeathSettlement(
                    canonicalNpc,
                    usesEaterOfWorldsTickDedupe))
            {
                return;
            }

            ModContent.GetInstance<StatisticsWorldSystem>().RecordNpcDeath(content);
            if (lastInteraction < 0 || lastInteraction >= Main.maxPlayers)
            {
                return;
            }

            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                RecordSinglePlayerFinalKill(lastInteraction, content);
                return;
            }

            if (Main.netMode == NetmodeID.Server)
            {
                SendMultiplayerCombatCredits(canonicalNpc, lastInteraction, content);
            }
        }

        /// <summary>
        /// 原子式标记主体已开始结算，防止同一多部位死亡重复增加世界或角色统计。
        /// </summary>
        private static bool TryBeginDeathSettlement(
            NPC canonicalNpc,
            bool usesEaterOfWorldsTickDedupe)
        {
            if (usesEaterOfWorldsTickDedupe)
            {
                // 世界吞噬怪的多个节段可能在同一更新刻依次回调 OnKill。
                if (lastEaterOfWorldsDeathTick == Main.GameUpdateCount)
                {
                    return false;
                }

                lastEaterOfWorldsDeathTick = Main.GameUpdateCount;
                return true;
            }

            StatisticsGlobalNPC canonicalTracker =
                canonicalNpc.GetGlobalNPC<StatisticsGlobalNPC>();
            if (canonicalTracker.deathRecorded)
            {
                return false;
            }

            canonicalTracker.deathRecorded = true;
            return true;
        }

        /// <summary>
        /// 在单人模式中把最后交互玩家结算为一次最终击杀。
        /// </summary>
        private static void RecordSinglePlayerFinalKill(
            int lastInteraction,
            StableContentKey content)
        {
            if (lastInteraction < 0 || lastInteraction >= Main.maxPlayers)
            {
                return;
            }

            Player killer = Main.player[lastInteraction];
            if (killer?.active == true && killer.whoAmI == Main.myPlayer)
            {
                killer.GetModPlayer<StatisticsPlayer>().RecordNpcFinalKill(content);
            }
        }

        /// <summary>
        /// 由服务器向所有在线参与者分发互斥的最终击杀或协助击杀结果。
        /// </summary>
        private static void SendMultiplayerCombatCredits(
            NPC canonicalNpc,
            int lastInteraction,
            StableContentKey content)
        {
            bool[] playerInteractions = canonicalNpc.playerInteraction;
            for (int playerIndex = 0; playerIndex < Main.maxPlayers; playerIndex++)
            {
                bool isFinalKiller = playerIndex == lastInteraction;
                bool participated = isFinalKiller
                    || (playerInteractions != null
                        && playerIndex < playerInteractions.Length
                        && playerInteractions[playerIndex]);
                if (!participated || Main.player[playerIndex]?.active != true)
                {
                    continue;
                }

                StatisticsNetcode.SendNpcCombatCredit(
                    playerIndex,
                    content,
                    isFinalKiller
                        ? NpcCombatCreditKind.FinalKill
                        : NpcCombatCreditKind.Assist);
            }
        }
    }
}
