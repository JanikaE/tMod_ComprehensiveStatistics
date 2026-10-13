using System;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ComprehensiveStatistics.Common.Players
{
    /// <summary>
    /// 拥有角色统计数据，并为采集器提供同时写入生涯与当前世界切片的统一入口。
    /// </summary>
    internal sealed class StatisticsPlayer : ModPlayer
    {
        private Guid currentWorldId;
        private StatisticsData currentWorldSlice;
        private bool hasRecordedCurrentDeath;

        internal CharacterStatisticsData Data { get; private set; }

        internal StatisticsData CurrentWorldSlice => currentWorldSlice;

        public override void Initialize()
        {
            Data = new CharacterStatisticsData(DateTimeOffset.UtcNow);
            currentWorldId = Guid.Empty;
            currentWorldSlice = null;
            hasRecordedCurrentDeath = false;
        }

        public override void OnEnterWorld()
        {
            EnsureCurrentWorldSlice();
            hasRecordedCurrentDeath = false;

            // B2 临时验收计数只由所属客户端写入，进入世界一次即同时增长两个角色范围。
            if (IsOwnedByLocalClient)
            {
                Record(InternalStatisticKeys.B2WorldEntries, new StatisticMetrics(events: 1L));
            }
        }

        public override void SaveData(TagCompound tag)
        {
            tag[StatisticsDataSerializer.PlayerTagKey] = StatisticsDataSerializer.Serialize(Data);
        }

        public override void LoadData(TagCompound tag)
        {
            if (StatisticsDataSerializer.TryDeserialize(
                tag,
                out CharacterStatisticsData loadedData,
                out string error))
            {
                Data = loadedData;
            }
            else if (!string.IsNullOrEmpty(error))
            {
                Mod.Logger.Warn($"Ignoring invalid character statistics data. {error}");
            }

            // OnEnterWorld 会根据当前世界 UUID 重新连接到已加载的对应切片。
            currentWorldId = Guid.Empty;
            currentWorldSlice = null;
            hasRecordedCurrentDeath = false;
        }

        public override void Kill(
            double damage,
            int hitDirection,
            bool pvp,
            PlayerDeathReason damageSource)
        {
            // Kill 会在本地、服务器和远程客户端触发。个人统计只由死亡玩家所属客户端提交，
            // 从源头避免同一次死亡因网络副本重复累计。
            if (!IsOwnedByLocalClient || hasRecordedCurrentDeath)
            {
                return;
            }

            hasRecordedCurrentDeath = true;
            Record(StatisticKeys.PlayerDeaths, new StatisticMetrics(events: 1L));
        }

        public override void OnRespawn()
        {
            hasRecordedCurrentDeath = false;
        }

        internal void Record(StatisticKey key, StatisticMetrics delta)
        {
            EnsureCurrentWorldSlice();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Data.Add(currentWorldId, key, delta, now);
            currentWorldSlice = Data.GetOrCreateWorldSlice(currentWorldId, now);
        }

        private void EnsureCurrentWorldSlice()
        {
            Guid activeWorldId = Main.ActiveWorldFileData?.UniqueId ?? Guid.Empty;
            if (currentWorldSlice != null && currentWorldId == activeWorldId)
            {
                return;
            }

            currentWorldId = activeWorldId;
            currentWorldSlice = Data.GetOrCreateWorldSlice(activeWorldId, DateTimeOffset.UtcNow);
        }

        private bool IsOwnedByLocalClient =>
            Main.netMode != NetmodeID.Server && Player.whoAmI == Main.myPlayer;
    }
}
