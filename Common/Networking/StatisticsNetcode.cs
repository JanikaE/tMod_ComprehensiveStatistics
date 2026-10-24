using System;
using System.Collections.Generic;
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
        ReservedLegacyWorldNpcDeath = 2,
        WiringChangeVerificationRequest = 3,
        WiringChangeVerificationResult = 4,
        WorldNpcDeathBatch = 5,
        WorldStatisticsSnapshotRequest = 6,
        WorldStatisticsSnapshot = 7
    }

    /// <summary>
    /// 描述客户端在一次本地布线工具调用中观察到的一个真实组件状态变化。
    /// </summary>
    internal readonly struct WiringChangeVerificationEntry
    {
        internal WiringChangeVerificationEntry(
            int x,
            int y,
            WiringComponentKind component,
            bool expectedPresent)
        {
            X = x;
            Y = y;
            Component = component;
            ExpectedPresent = expectedPresent;
        }

        internal int X { get; }

        internal int Y { get; }

        internal WiringComponentKind Component { get; }

        internal bool ExpectedPresent { get; }
    }

    /// <summary>
    /// 描述一个带服务器单调修订号的世界 NPC 死亡增量。
    /// </summary>
    internal readonly struct WorldNpcDeathDelta
    {
        internal WorldNpcDeathDelta(ulong revision, StableContentKey content)
        {
            Revision = revision;
            Content = content;
        }

        internal ulong Revision { get; }

        internal StableContentKey Content { get; }
    }

    /// <summary>
    /// 负责角色归属结果、服务器权威世界统计和多人布线确认的网络同步。
    /// </summary>
    internal static class StatisticsNetcode
    {
        private const int MaximumWiringChangesPerPacket = 512;
        private const int MaximumWorldDeltasPerPacket = 512;
        private const int WiringComponentCount = 5;

        /// <summary>
        /// 向指定在线玩家发送一次服务器确认的最终击杀或协助击杀。
        /// </summary>
        internal static void SendNpcCombatCredit(
            int playerIndex,
            StableContentKey content,
            NpcCombatCreditKind creditKind)
        {
            if (!IsActiveServerPlayer(playerIndex))
            {
                return;
            }

            ModPacket packet = CreatePacket(StatisticsPacketType.NpcCombatCredit);
            packet.Write((byte)creditKind);
            packet.Write(content.ModName);
            packet.Write(content.ContentName);
            packet.Send(playerIndex);
        }

        /// <summary>
        /// 将服务器积累的世界 NPC 死亡增量按有限大小的数据包广播给全部客户端。
        /// </summary>
        internal static void BroadcastWorldNpcDeathBatch(
            IReadOnlyList<WorldNpcDeathDelta> deltas)
        {
            if (Main.netMode != NetmodeID.Server || deltas == null)
            {
                return;
            }

            for (int offset = 0; offset < deltas.Count; offset += MaximumWorldDeltasPerPacket)
            {
                int count = Math.Min(MaximumWorldDeltasPerPacket, deltas.Count - offset);
                ModPacket packet = CreatePacket(StatisticsPacketType.WorldNpcDeathBatch);
                packet.Write(count);
                for (int index = 0; index < count; index++)
                {
                    WorldNpcDeathDelta delta = deltas[offset + index];
                    packet.Write(delta.Revision);
                    packet.Write(delta.Content.ModName);
                    packet.Write(delta.Content.ContentName);
                }

                packet.Send();
            }
        }

        /// <summary>
        /// 将本地布线工具产生的候选变化分块发送给服务器进行权威格位验证。
        /// </summary>
        internal static void RequestWiringChangeVerification(
            IReadOnlyList<WiringChangeVerificationEntry> changes)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient || changes == null)
            {
                return;
            }

            for (int offset = 0; offset < changes.Count; offset += MaximumWiringChangesPerPacket)
            {
                int count = Math.Min(MaximumWiringChangesPerPacket, changes.Count - offset);
                ModPacket packet = CreatePacket(
                    StatisticsPacketType.WiringChangeVerificationRequest);
                packet.Write(count);
                for (int index = 0; index < count; index++)
                {
                    WiringChangeVerificationEntry change = changes[offset + index];
                    packet.Write(change.X);
                    packet.Write(change.Y);
                    packet.Write((byte)change.Component);
                    packet.Write(change.ExpectedPresent);
                }

                packet.Send();
            }
        }

        /// <summary>
        /// 请求服务器重新发送当前世界公共统计快照；用于打开界面及修订缺口恢复。
        /// </summary>
        internal static void RequestWorldStatisticsSnapshot()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                return;
            }

            CreatePacket(StatisticsPacketType.WorldStatisticsSnapshotRequest).Send();
        }

        /// <summary>
        /// 根据当前网络端处理统计包；客户端不能直接提交角色或世界统计数值。
        /// </summary>
        internal static void HandlePacket(BinaryReader reader, int whoAmI)
        {
            if (reader == null)
            {
                return;
            }

            StatisticsPacketType packetType = (StatisticsPacketType)reader.ReadByte();
            if (Main.netMode == NetmodeID.Server)
            {
                HandleServerPacket(packetType, reader, whoAmI);
                return;
            }

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                HandleClientPacket(packetType, reader);
            }
        }

        /// <summary>
        /// 分派仅允许从客户端发往服务器的验证和快照请求。
        /// </summary>
        private static void HandleServerPacket(
            StatisticsPacketType packetType,
            BinaryReader reader,
            int whoAmI)
        {
            if (!IsActiveServerPlayer(whoAmI))
            {
                return;
            }

            if (packetType == StatisticsPacketType.WiringChangeVerificationRequest)
            {
                HandleWiringChangeVerificationRequest(reader, whoAmI);
            }
            else if (packetType == StatisticsPacketType.WorldStatisticsSnapshotRequest)
            {
                SendWorldStatisticsSnapshot(whoAmI);
            }
        }

        /// <summary>
        /// 分派仅允许由服务器下发给客户端的权威结果。
        /// </summary>
        private static void HandleClientPacket(
            StatisticsPacketType packetType,
            BinaryReader reader)
        {
            switch (packetType)
            {
                case StatisticsPacketType.NpcCombatCredit:
                    HandleNpcCombatCredit(reader);
                    break;
                case StatisticsPacketType.WiringChangeVerificationResult:
                    HandleWiringChangeVerificationResult(reader);
                    break;
                case StatisticsPacketType.WorldNpcDeathBatch:
                    HandleWorldNpcDeathBatch(reader);
                    break;
                case StatisticsPacketType.WorldStatisticsSnapshot:
                    HandleWorldStatisticsSnapshot(reader);
                    break;
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
        /// 在服务器按最终权威格位状态筛选客户端候选变化，并仅回传确认后的分类数量。
        /// </summary>
        private static void HandleWiringChangeVerificationRequest(
            BinaryReader reader,
            int playerIndex)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumWiringChangesPerPacket)
            {
                return;
            }

            long[] placedCounts = new long[WiringComponentCount];
            long[] removedCounts = new long[WiringComponentCount];
            for (int index = 0; index < count; index++)
            {
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                WiringComponentKind component = (WiringComponentKind)reader.ReadByte();
                bool expectedPresent = reader.ReadBoolean();
                int componentIndex = (int)component;
                if (componentIndex < 0
                    || componentIndex >= WiringComponentCount
                    || !StatisticsWiringSystem.ConsumeServerAcceptedChange(
                        playerIndex,
                        x,
                        y,
                        component,
                        expectedPresent))
                {
                    continue;
                }

                long[] counts = expectedPresent ? placedCounts : removedCounts;
                counts[componentIndex]++;
            }

            SendWiringChangeVerificationResult(
                playerIndex,
                placedCounts,
                removedCounts);
        }

        /// <summary>
        /// 将服务器确认后的布线分类数量回传给发起操作的唯一客户端。
        /// </summary>
        private static void SendWiringChangeVerificationResult(
            int playerIndex,
            long[] placedCounts,
            long[] removedCounts)
        {
            bool hasChanges = false;
            for (int index = 0; index < WiringComponentCount; index++)
            {
                hasChanges |= placedCounts[index] > 0L || removedCounts[index] > 0L;
            }

            if (!hasChanges || !IsActiveServerPlayer(playerIndex))
            {
                return;
            }

            ModPacket packet = CreatePacket(
                StatisticsPacketType.WiringChangeVerificationResult);
            for (int index = 0; index < WiringComponentCount; index++)
            {
                packet.Write(placedCounts[index]);
                packet.Write(removedCounts[index]);
            }

            packet.Send(playerIndex);
        }

        /// <summary>
        /// 在发起操作的客户端提交服务器已经确认的布线分类数量。
        /// </summary>
        private static void HandleWiringChangeVerificationResult(BinaryReader reader)
        {
            if (Main.LocalPlayer?.active != true)
            {
                return;
            }

            StatisticsPlayer statisticsPlayer =
                Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
            for (int componentIndex = 0;
                componentIndex < WiringComponentCount;
                componentIndex++)
            {
                long placedCount = reader.ReadInt64();
                long removedCount = reader.ReadInt64();
                WiringComponentKind component = (WiringComponentKind)componentIndex;
                statisticsPlayer.RecordWiringComponentChanges(
                    component,
                    placed: true,
                    placedCount);
                statisticsPlayer.RecordWiringComponentChanges(
                    component,
                    placed: false,
                    removedCount);
            }
        }

        /// <summary>
        /// 在客户端按服务器修订顺序应用一批世界 NPC 死亡增量。
        /// </summary>
        private static void HandleWorldNpcDeathBatch(BinaryReader reader)
        {
            StatisticsWorldSystem worldSystem =
                ModContent.GetInstance<StatisticsWorldSystem>();
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumWorldDeltasPerPacket)
            {
                worldSystem.RequestSnapshotAfterInvalidDelta();
                return;
            }

            for (int index = 0; index < count; index++)
            {
                ulong revision = reader.ReadUInt64();
                string modName = reader.ReadString();
                string contentName = reader.ReadString();
                if (string.IsNullOrWhiteSpace(modName)
                    || string.IsNullOrWhiteSpace(contentName))
                {
                    worldSystem.RequestSnapshotAfterInvalidDelta();
                    continue;
                }

                worldSystem.ApplySynchronizedNpcDeath(
                    revision,
                    new StableContentKey(modName, contentName));
            }
        }

        /// <summary>
        /// 向指定客户端发送当前服务器修订号和完整世界公共统计。
        /// </summary>
        private static void SendWorldStatisticsSnapshot(int playerIndex)
        {
            if (!IsActiveServerPlayer(playerIndex))
            {
                return;
            }

            StatisticsWorldSystem worldSystem =
                ModContent.GetInstance<StatisticsWorldSystem>();
            ModPacket packet = CreatePacket(StatisticsPacketType.WorldStatisticsSnapshot);
            packet.Write(worldSystem.WorldRevision);
            StatisticsDataSerializer.WriteScope(packet, worldSystem.Data);
            packet.Send(playerIndex);
        }

        /// <summary>
        /// 在客户端用服务器完整快照替换当前世界镜像。
        /// </summary>
        private static void HandleWorldStatisticsSnapshot(BinaryReader reader)
        {
            ulong revision = reader.ReadUInt64();
            if (StatisticsDataSerializer.TryReadScope(
                reader,
                out StatisticsData synchronizedData,
                out string error))
            {
                ModContent.GetInstance<StatisticsWorldSystem>()
                    .ApplySynchronizedSnapshot(revision, synchronizedData);
            }
            else
            {
                ModContent
                    .GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>()
                    .Logger.Warn(
                        $"Ignoring invalid synchronized world statistics snapshot. {error}");
            }
        }

        /// <summary>
        /// 创建已写入包类型的模组数据包。
        /// </summary>
        private static ModPacket CreatePacket(StatisticsPacketType packetType)
        {
            ModPacket packet = ModContent
                .GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>()
                .GetPacket();
            packet.Write((byte)packetType);
            return packet;
        }

        /// <summary>
        /// 判断索引是否指向当前服务器上的有效在线玩家。
        /// </summary>
        private static bool IsActiveServerPlayer(int playerIndex)
        {
            return Main.netMode == NetmodeID.Server
                && playerIndex >= 0
                && playerIndex < Main.maxPlayers
                && Main.player[playerIndex]?.active == true;
        }
    }
}
