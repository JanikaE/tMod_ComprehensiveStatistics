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
        private bool hasLifeSnapshot;
        private int previousLifeSnapshot;
        private long pendingDirectLifeLost;
        private bool hasPendingDirectDeathSource;
        private DirectDamageSourceKind pendingDirectDeathSource;
        private StableContentKey? pendingDirectDeathNpc;
        private StatisticKey? pendingContinuousDeathSource;

        internal CharacterStatisticsData Data { get; private set; }

        internal StatisticsData CurrentWorldSlice => currentWorldSlice;

        public override void Initialize()
        {
            Data = new CharacterStatisticsData(DateTimeOffset.UtcNow);
            currentWorldId = Guid.Empty;
            currentWorldSlice = null;
            hasRecordedCurrentDeath = false;
            hasLifeSnapshot = false;
            previousLifeSnapshot = 0;
            pendingDirectLifeLost = 0L;
            ClearPendingDeathSources();
        }

        public override void OnEnterWorld()
        {
            EnsureCurrentWorldSlice();
            hasRecordedCurrentDeath = false;
            ResetLifeLossTracking();

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
            hasLifeSnapshot = false;
            previousLifeSnapshot = 0;
            pendingDirectLifeLost = 0L;
            ClearPendingDeathSources();
        }

        public override void Kill(
            double damage,
            int hitDirection,
            bool pvp,
            PlayerDeathReason damageSource)
        {
            // Kill 会在本地、服务器和远程客户端触发。个人统计只由死亡玩家所属客户端提交，
            // 从源头避免同一次死亡因网络副本重复累计。
            if (!IsOwnedByLocalClient)
            {
                return;
            }

            // 持续生命流失致死后不一定还有完整的 PostUpdate，因此先结算最后一段生命。
            ReconcileLifeLoss();
            if (hasRecordedCurrentDeath)
            {
                return;
            }

            hasRecordedCurrentDeath = true;
            Record(StatisticKeys.PlayerDeaths, new StatisticMetrics(events: 1L));
            RecordDeathSource(damageSource, pvp);
            ClearPendingDeathSources();
        }

        public override void OnHurt(Player.HurtInfo info)
        {
            // OnHurt 只在受伤玩家的本地客户端触发，且此时 statLife 仍是扣血前的值。
            // HurtInfo.Damage 是最终伤害但不按剩余生命封顶，因此致死伤害必须在此截断。
            if (!IsOwnedByLocalClient)
            {
                return;
            }

            int actualLifeLost = Math.Min(Math.Max(Player.statLife, 0), info.Damage);
            if (actualLifeLost > 0)
            {
                EnsureLifeSnapshot();
                pendingDirectLifeLost = SaturatingAdd(
                    pendingDirectLifeLost,
                    actualLifeLost);

                DirectDamageSourceKind source = DirectDamageSourceClassifier.Classify(
                    info,
                    out NPC npcSource);
                StableContentKey? npcKey = null;
                Record(
                    StatisticKeys.DirectLifeLost,
                    new StatisticMetrics(amount: actualLifeLost));
                Record(
                    StatisticKeys.GetDirectDamageSourceKey(source),
                    new StatisticMetrics(amount: actualLifeLost));

                if (npcSource != null
                    && StableContentKey.TryCreateForNpc(
                        npcSource,
                        out StableContentKey resolvedNpcKey))
                {
                    npcKey = resolvedNpcKey;
                    Record(
                        StatisticKeys.GetNpcDirectDamageKey(resolvedNpcKey),
                        new StatisticMetrics(amount: actualLifeLost));
                }

                if (actualLifeLost >= Math.Max(Player.statLife, 0))
                {
                    hasPendingDirectDeathSource = true;
                    pendingDirectDeathSource = source;
                    pendingDirectDeathNpc = npcKey;
                    pendingContinuousDeathSource = null;
                }
            }
        }

        public override void PostUpdate()
        {
            if (IsOwnedByLocalClient)
            {
                ReconcileLifeLoss();
            }
        }

        public override void OnRespawn()
        {
            hasRecordedCurrentDeath = false;
            ResetLifeLossTracking();
        }

        /// <summary>
        /// 将一次统计增量提交到角色生涯及当前世界切片。
        /// </summary>
        internal void Record(StatisticKey key, StatisticMetrics delta)
        {
            EnsureCurrentWorldSlice();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Data.Add(currentWorldId, key, delta, now);
            currentWorldSlice = Data.GetOrCreateWorldSlice(currentWorldId, now);
        }

        /// <summary>
        /// 确保缓存的世界统计切片与当前世界唯一标识一致。
        /// </summary>
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

        /// <summary>
        /// 在首次参与生命差额结算前建立基准快照。
        /// </summary>
        private void EnsureLifeSnapshot()
        {
            if (hasLifeSnapshot)
            {
                return;
            }

            previousLifeSnapshot = Math.Max(Player.statLife, 0);
            pendingDirectLifeLost = 0L;
            hasLifeSnapshot = true;
            ClearPendingDeathSources();
        }

        /// <summary>
        /// 以玩家当前生命值重置直接伤害和致死来源的临时状态。
        /// </summary>
        private void ResetLifeLossTracking()
        {
            previousLifeSnapshot = Math.Max(Player.statLife, 0);
            pendingDirectLifeLost = 0L;
            hasLifeSnapshot = true;
            ClearPendingDeathSources();
        }

        /// <summary>
        /// 对比相邻帧生命快照，补记未经过普通受伤回调的生命损失。
        /// </summary>
        private void ReconcileLifeLoss()
        {
            EnsureLifeSnapshot();

            int currentLife = Math.Max(Player.statLife, 0);
            long observedLifeLost = Math.Max((long)previousLifeSnapshot - currentLife, 0L);
            long unreportedLifeLost = Math.Max(
                observedLifeLost - pendingDirectLifeLost,
                0L);

            if (unreportedLifeLost > 0L)
            {
                if (IsDrowningLifeLoss())
                {
                    // 原版溺水直接修改 statLife，不触发 OnHurt；在快照差额中优先回填到
                    // C2 的互斥直接伤害类别，避免落入 C3 的未知生命流失。
                    Record(
                        StatisticKeys.DirectLifeLost,
                        new StatisticMetrics(amount: unreportedLifeLost));
                    Record(
                        StatisticKeys.GetDirectDamageSourceKey(
                            DirectDamageSourceKind.Drowning),
                        new StatisticMetrics(amount: unreportedLifeLost));

                    if (currentLife <= 0)
                    {
                        hasPendingDirectDeathSource = true;
                        pendingDirectDeathSource = DirectDamageSourceKind.Drowning;
                        pendingDirectDeathNpc = null;
                        pendingContinuousDeathSource = null;
                    }
                }
                else
                {
                    StatisticKey deathSource = RecordContinuousLifeLoss(unreportedLifeLost);
                    if (currentLife <= 0)
                    {
                        pendingContinuousDeathSource = deathSource;
                    }
                }
            }

            // 直接伤害可能与同周期治疗相抵；周期结束后仍须清空，不能挪去抵消下一次流失。
            previousLifeSnapshot = currentLife;
            pendingDirectLifeLost = 0L;
            if (!Player.dead)
            {
                // 兼容其他模组取消死亡或立即救活玩家的情况，避免保留一次未发生的致死来源。
                ClearPendingDeathSources();
            }
        }

        /// <summary>
        /// 判断当前未报告生命损失是否符合原版溺水条件。
        /// </summary>
        private bool IsDrowningLifeLoss()
        {
            // breath 为 0 后才会产生溺水扣血；排除不会使用普通呼吸规则的液体与形态。
            return Player.breath <= 0
                && Player.wet
                && !Player.lavaWet
                && !Player.shimmerWet
                && !Player.gills
                && !Player.merman;
        }

        /// <summary>
        /// 记录一段持续生命损失，并返回同来源的死亡统计键。
        /// </summary>
        private StatisticKey RecordContinuousLifeLoss(long amount)
        {
            ContinuousLifeLossSourceKind source = ContinuousLifeLossClassifier.Classify(
                Player,
                out int singleDebuffType);
            StatisticKey key;
            StatisticKey deathKey;

            if (source == ContinuousLifeLossSourceKind.SingleDebuff
                && StableContentKey.TryCreateForBuff(
                    singleDebuffType,
                    out StableContentKey buffKey))
            {
                key = StatisticKeys.GetContinuousDamageDebuffKey(buffKey);
                deathKey = StatisticKeys.GetContinuousDamageDebuffDeathKey(buffKey);
            }
            else if (source == ContinuousLifeLossSourceKind.MultipleDebuffs)
            {
                key = StatisticKeys.MultipleContinuousDamageEffects;
                deathKey = StatisticKeys.MultipleContinuousDamageDeaths;
            }
            else
            {
                key = StatisticKeys.UnknownContinuousLifeLoss;
                deathKey = StatisticKeys.UnknownContinuousLifeLossDeaths;
            }

            Record(key, new StatisticMetrics(amount: amount));
            return deathKey;
        }

        /// <summary>
        /// 按已捕获来源、死亡原因和未知来源的优先级记录一次死亡归因。
        /// </summary>
        private void RecordDeathSource(PlayerDeathReason reason, bool pvp)
        {
            if (hasPendingDirectDeathSource)
            {
                Record(
                    StatisticKeys.GetDirectDeathSourceKey(pendingDirectDeathSource),
                    new StatisticMetrics(events: 1L));
                if (pendingDirectDeathNpc.HasValue)
                {
                    Record(
                        StatisticKeys.GetNpcDirectDeathKey(pendingDirectDeathNpc.Value),
                        new StatisticMetrics(events: 1L));
                }

                return;
            }

            if (pendingContinuousDeathSource.HasValue)
            {
                Record(
                    pendingContinuousDeathSource.Value,
                    new StatisticMetrics(events: 1L));
                return;
            }

            if (DirectDamageSourceClassifier.TryClassifyDeathReason(
                reason,
                pvp,
                out DirectDamageSourceKind directSource,
                out NPC npcSource))
            {
                Record(
                    StatisticKeys.GetDirectDeathSourceKey(directSource),
                    new StatisticMetrics(events: 1L));
                if (npcSource != null
                    && StableContentKey.TryCreateForNpc(
                        npcSource,
                        out StableContentKey npcKey))
                {
                    Record(
                        StatisticKeys.GetNpcDirectDeathKey(npcKey),
                        new StatisticMetrics(events: 1L));
                }

                return;
            }

            Record(StatisticKeys.UnknownDeathSource, new StatisticMetrics(events: 1L));
        }

        /// <summary>
        /// 清除仅对当前生命结算周期有效的候选致死来源。
        /// </summary>
        private void ClearPendingDeathSources()
        {
            hasPendingDirectDeathSource = false;
            pendingDirectDeathSource = default;
            pendingDirectDeathNpc = null;
            pendingContinuousDeathSource = null;
        }

        /// <summary>
        /// 安全累加生命损失，避免极端模组数值造成整数溢出。
        /// </summary>
        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private bool IsOwnedByLocalClient =>
            Main.netMode != NetmodeID.Server && Player.whoAmI == Main.myPlayer;
    }
}
