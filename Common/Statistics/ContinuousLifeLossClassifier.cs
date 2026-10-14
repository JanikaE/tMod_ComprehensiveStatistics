using Terraria;
using Terraria.ID;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 根据生命减少发生时仍生效的明确伤害型 Debuff，对非直接伤害的生命流失进行互斥分类。
    /// </summary>
    internal static class ContinuousLifeLossClassifier
    {
        /// <summary>
        /// 将当前生效的持续伤害效果归为单一 Debuff、多重 Debuff 或未知来源。
        /// </summary>
        internal static ContinuousLifeLossSourceKind Classify(
            Player player,
            out int singleDebuffType)
        {
            singleDebuffType = 0;

            for (int index = 0; index < player.buffType.Length; index++)
            {
                if (player.buffTime[index] <= 0)
                {
                    continue;
                }

                int buffType = player.buffType[index];
                if (!IsKnownDamageOverTimeBuff(buffType) || buffType == singleDebuffType)
                {
                    continue;
                }

                if (singleDebuffType != 0)
                {
                    singleDebuffType = 0;
                    return ContinuousLifeLossSourceKind.MultipleDebuffs;
                }

                singleDebuffType = buffType;
            }

            return singleDebuffType != 0
                ? ContinuousLifeLossSourceKind.SingleDebuff
                : ContinuousLifeLossSourceKind.Unknown;
        }

        /// <summary>
        /// 判断 Buff 是否是已知会直接造成生命流失的原版效果。
        /// </summary>
        private static bool IsKnownDamageOverTimeBuff(int buffType)
        {
            // Main.debuff 还包含减速、沉默等不造成生命损失的效果，不能直接用作判断。
            // 未提供明确伤害声明的模组 Debuff 会安全地落入“未知生命流失”。
            return buffType == BuffID.Poisoned
                || buffType == BuffID.OnFire
                || buffType == BuffID.CursedInferno
                || buffType == BuffID.Frostburn
                || buffType == BuffID.Burning
                || buffType == BuffID.Suffocation
                || buffType == BuffID.Venom
                || buffType == BuffID.Electrified
                || buffType == BuffID.ShadowFlame
                || buffType == BuffID.TheTongue
                || buffType == BuffID.OnFire3
                || buffType == BuffID.Frostburn2
                || buffType == BuffID.Starving;
        }
    }
}
