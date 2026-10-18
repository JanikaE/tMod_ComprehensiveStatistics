using System;
using System.IO;
using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Systems;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Networking
{
    /// <summary>
    /// 标识由服务器下发给角色所属客户端的 NPC 战斗结果。
    /// </summary>
    internal enum NpcCombatCreditKind : byte
    {
        FinalKill = 1,
        Assist = 2
    }

    /// <summary>
    /// 定义模组网络包类型；已有数值不得复用，以保持版本诊断清晰。
    /// </summary>
    internal enum StatisticsPacketType : byte
    {
        NpcCombatCredit = 1,
        WorldNpcDeath = 2
    }

    /// <summary>
    /// 负责服务器到客户端的稀疏统计结果同步，不接受客户端提交统计增量。
    /// </summary>
    internal static class StatisticsNetcode
    {
        /// <summary>
        /// 向指定在线玩家发送一次服务器确认的最终击杀或协助击杀。
        /// </summary>
        internal static void SendNpcCombatCredit(
            int playerIndex,
            StableContentKey content,
            NpcCombatCreditKind creditKind)
        {
            if (Main.netMode != NetmodeID.Server
                || playerIndex < 0
                || playerIndex >= Main.maxPlayers
                || Main.player[playerIndex]?.active != true)
            {
                return;
            }

            ModPacket packet = ModContent
                .GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>()
                .GetPacket();
            packet.Write((byte)StatisticsPacketType.NpcCombatCredit);
            packet.Write((byte)creditKind);
            packet.Write(content.ModName);
            packet.Write(content.ContentName);
            packet.Send(playerIndex);
        }

        /// <summary>
        /// 向所有在线客户端广播一次已由服务器去重的 NPC 世界死亡增量。
        /// </summary>
        internal static void BroadcastWorldNpcDeath(StableContentKey content)
        {
            if (Main.netMode != NetmodeID.Server)
            {
                return;
            }

            ModPacket packet = ModContent
                .GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>()
                .GetPacket();
            packet.Write((byte)StatisticsPacketType.WorldNpcDeath);
            packet.Write(content.ModName);
            packet.Write(content.ContentName);
            packet.Send();
        }

        /// <summary>
        /// 处理服务器下发的统计结果；服务器收到任何此类入站包时一律忽略。
        /// </summary>
        internal static void HandlePacket(BinaryReader reader, int whoAmI)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient || reader == null)
            {
                return;
            }

            StatisticsPacketType packetType = (StatisticsPacketType)reader.ReadByte();
            if (packetType == StatisticsPacketType.NpcCombatCredit)
            {
                HandleNpcCombatCredit(reader);
                return;
            }

            if (packetType == StatisticsPacketType.WorldNpcDeath)
            {
                HandleWorldNpcDeath(reader);
            }
        }

        /// <summary>
        /// 在角色所属客户端应用一次最终击杀或协助击杀结果。
        /// </summary>
        private static void HandleNpcCombatCredit(BinaryReader reader)
        {
            NpcCombatCreditKind creditKind = (NpcCombatCreditKind)reader.ReadByte();
            string modName = reader.ReadString();
            string contentName = reader.ReadString();
            if (!Enum.IsDefined(typeof(NpcCombatCreditKind), creditKind)
                || string.IsNullOrWhiteSpace(modName)
                || string.IsNullOrWhiteSpace(contentName)
                || Main.LocalPlayer?.active != true)
            {
                return;
            }

            StableContentKey content = new StableContentKey(modName, contentName);
            StatisticsPlayer statisticsPlayer =
                Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            if (creditKind == NpcCombatCreditKind.FinalKill)
            {
                statisticsPlayer.RecordNpcFinalKill(content);
            }
            else if (creditKind == NpcCombatCreditKind.Assist)
            {
                statisticsPlayer.RecordNpcAssist(content);
            }
        }

        /// <summary>
        /// 在客户端镜像服务器确认的一次 NPC 世界死亡增量。
        /// </summary>
        private static void HandleWorldNpcDeath(BinaryReader reader)
        {
            string modName = reader.ReadString();
            string contentName = reader.ReadString();
            if (string.IsNullOrWhiteSpace(modName)
                || string.IsNullOrWhiteSpace(contentName))
            {
                return;
            }

            ModContent.GetInstance<StatisticsWorldSystem>().ApplySynchronizedNpcDeath(
                new StableContentKey(modName, contentName));
        }
    }
}
