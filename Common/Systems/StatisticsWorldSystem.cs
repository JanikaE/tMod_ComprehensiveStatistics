using System;
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
        internal StatisticsData Data { get; private set; }

        public override void OnWorldLoad()
        {
            Data = new StatisticsData(DateTimeOffset.UtcNow);
        }

        public override void OnWorldUnload()
        {
            Data = null;
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
            StatisticsDataSerializer.WriteScope(writer, Data);
        }

        public override void NetReceive(BinaryReader reader)
        {
            if (StatisticsDataSerializer.TryReadScope(
                reader,
                out StatisticsData synchronizedData,
                out string error))
            {
                Data = synchronizedData;
                ModContent.GetInstance<StatisticsUISystem>().RefreshWorldPublicData();
            }
            else
            {
                Mod.Logger.Warn($"Ignoring invalid synchronized world statistics data. {error}");
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
                StatisticsNetcode.BroadcastWorldNpcDeath(content);
            }
            else
            {
                ModContent.GetInstance<StatisticsUISystem>().RefreshWorldPublicData();
            }
        }

        /// <summary>
        /// 在多人客户端应用服务器下发的一次 NPC 世界死亡增量。
        /// </summary>
        internal void ApplySynchronizedNpcDeath(StableContentKey content)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                return;
            }

            Data ??= new StatisticsData(DateTimeOffset.UtcNow);
            Data.Add(
                StatisticKeys.GetNpcWorldDeathKey(content),
                new StatisticMetrics(events: 1L));
            ModContent.GetInstance<StatisticsUISystem>().RefreshWorldPublicData();
        }
    }
}
