using System;
using System.Collections.Generic;
using System.IO;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 持有、保存并同步当前世界的公共统计范围。
    /// </summary>
    internal sealed class StatisticsWorldSystem : ModSystem
    {
        private const int WorldDeltaFlushIntervalTicks = 30;
        private const int MaximumPendingWorldDeltas = 512;

        private readonly List<WorldNpcDeathDelta> pendingNpcDeathDeltas =
            new List<WorldNpcDeathDelta>();
        private int worldDeltaFlushTicks;
        private ulong worldRevision;
        private ulong synchronizedWorldRevision;
        private bool awaitingWorldSnapshot;

        internal StatisticsData Data { get; private set; }

        internal ulong WorldRevision => worldRevision;

        public override void OnWorldLoad()
        {
            Data = new StatisticsData(DateTimeOffset.UtcNow);
            ResetNetworkState();
        }

        public override void OnWorldUnload()
        {
            Data = null;
            ResetNetworkState();
        }

        public override void SaveWorldData(TagCompound tag)
        {
            if (Data != null)
            {
                tag[StatisticsDataSerializer.WorldTagKey] =
                    StatisticsDataSerializer.SerializeWorld(Data);
            }
        }

        public override void LoadWorldData(TagCompound tag)
        {
            if (StatisticsDataSerializer.TryDeserializeWorld(
                tag,
                out StatisticsData loadedData,
                out string error))
            {
                Data = loadedData;
            }
            else if (!string.IsNullOrEmpty(error))
            {
                Mod.Logger.Warn($"Ignoring invalid world statistics data. {error}");
            }
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(worldRevision);
            StatisticsDataSerializer.WriteScope(writer, Data);
        }

        public override void NetReceive(BinaryReader reader)
        {
            ulong revision = reader.ReadUInt64();
            if (StatisticsDataSerializer.TryReadScope(
                reader,
                out StatisticsData synchronizedData,
                out string error))
            {
                ApplySynchronizedSnapshot(revision, synchronizedData);
            }
            else
            {
                Mod.Logger.Warn($"Ignoring invalid synchronized world statistics data. {error}");
            }
        }

        public override void PostUpdateWorld()
        {
            if (Main.netMode != NetmodeID.Server || pendingNpcDeathDeltas.Count == 0)
            {
                return;
            }

            worldDeltaFlushTicks++;
            if (worldDeltaFlushTicks >= WorldDeltaFlushIntervalTicks)
            {
                FlushPendingWorldDeltas();
            }
        }

        /// <summary>
        /// 在单人或服务器权威端记录一次归一化后的 NPC 世界死亡。
        /// </summary>
        internal void RecordNpcDeath(StableContentKey content)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                return;
            }

            Data ??= new StatisticsData(DateTimeOffset.UtcNow);
            Data.Add(
                StatisticKeys.GetNpcWorldDeathKey(content),
                new StatisticMetrics(events: 1L));
            if (Main.netMode == NetmodeID.Server)
            {
                if (worldRevision < ulong.MaxValue)
                {
                    worldRevision++;
                }

                pendingNpcDeathDeltas.Add(
                    new WorldNpcDeathDelta(worldRevision, content));
                if (pendingNpcDeathDeltas.Count >= MaximumPendingWorldDeltas)
                {
                    FlushPendingWorldDeltas();
                }
            }
            else
            {
                ModContent.GetInstance<StatisticsUISystem>().RefreshWorldPublicData();
            }
        }

        /// <summary>
        /// 在多人客户端应用服务器下发的一次 NPC 世界死亡增量。
        /// </summary>
        internal void ApplySynchronizedNpcDeath(
            ulong revision,
            StableContentKey content)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient
                || awaitingWorldSnapshot
                || revision <= synchronizedWorldRevision)
            {
                return;
            }

            if (revision != synchronizedWorldRevision + 1UL)
            {
                RequestSnapshotAfterInvalidDelta();
                return;
            }

            Data ??= new StatisticsData(DateTimeOffset.UtcNow);
            Data.Add(
                StatisticKeys.GetNpcWorldDeathKey(content),
                new StatisticMetrics(events: 1L));
            synchronizedWorldRevision = revision;
            ModContent.GetInstance<StatisticsUISystem>().RefreshWorldPublicData();
        }

        /// <summary>
        /// 在多人客户端用带修订号的服务器快照替换世界公共统计镜像。
        /// </summary>
        internal void ApplySynchronizedSnapshot(
            ulong revision,
            StatisticsData synchronizedData)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient || synchronizedData == null)
            {
                return;
            }

            Data = synchronizedData;
            synchronizedWorldRevision = revision;
            awaitingWorldSnapshot = false;
            ModContent.GetInstance<StatisticsUISystem>().RefreshWorldPublicData();
        }

        /// <summary>
        /// 在打开统计窗口时主动请求最新世界快照，并在返回前忽略可能与快照重叠的增量。
        /// </summary>
        internal void RequestSynchronizedSnapshot()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                return;
            }

            awaitingWorldSnapshot = true;
            StatisticsNetcode.RequestWorldStatisticsSnapshot();
        }

        /// <summary>
        /// 在增量损坏或修订不连续时仅发起一次恢复请求。
        /// </summary>
        internal void RequestSnapshotAfterInvalidDelta()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient || awaitingWorldSnapshot)
            {
                return;
            }

            RequestSynchronizedSnapshot();
        }

        /// <summary>
        /// 将服务器本轮积累的世界死亡事件合并为批量网络包并清空队列。
        /// </summary>
        private void FlushPendingWorldDeltas()
        {
            if (Main.netMode == NetmodeID.Server && pendingNpcDeathDeltas.Count > 0)
            {
                StatisticsNetcode.BroadcastWorldNpcDeathBatch(pendingNpcDeathDeltas);
            }

            pendingNpcDeathDeltas.Clear();
            worldDeltaFlushTicks = 0;
        }

        /// <summary>
        /// 清理切换世界或退出时不能跨世界复用的网络修订与待发队列。
        /// </summary>
        private void ResetNetworkState()
        {
            pendingNpcDeathDeltas.Clear();
            worldDeltaFlushTicks = 0;
            worldRevision = 0UL;
            synchronizedWorldRevision = 0UL;
            awaitingWorldSnapshot = false;
        }
    }
}
