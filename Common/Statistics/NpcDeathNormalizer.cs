using Terraria;
using Terraria.ID;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 将多部位 Boss、共享生命蠕虫和原版特殊组合归一为一次可统计的主体死亡。
    /// </summary>
    internal static class NpcDeathNormalizer
    {
        /// <summary>
        /// 尝试解析一次实际死亡对应的主体、稳定内容键和最后交互玩家。
        /// </summary>
        internal static bool TryResolveDeathTarget(
            NPC npc,
            out NPC canonicalNpc,
            out StableContentKey content,
            out int lastInteraction,
            out bool usesEaterOfWorldsTickDedupe)
        {
            canonicalNpc = null;
            content = default;
            lastInteraction = byte.MaxValue;
            usesEaterOfWorldsTickDedupe = false;
            if (npc == null || npc.type == NPCID.TargetDummy)
            {
                return false;
            }

            if (IsEaterOfWorldsSegment(npc.type))
            {
                if (HasAnotherActiveEaterOfWorldsSegment(npc)
                    || !StableContentKey.TryCreateForNpcType(
                        NPCID.EaterofWorldsHead,
                        out content))
                {
                    return false;
                }

                // 世界吞噬怪没有统一 realLife 主体；仅最后一个仍存活的节段代表整条蠕虫死亡。
                canonicalNpc = npc;
                lastInteraction = npc.lastInteraction;
                usesEaterOfWorldsTickDedupe = true;
                return true;
            }

            if (IsNonCanonicalVanillaPart(npc.type))
            {
                return false;
            }

            canonicalNpc = ResolveRealLifeRoot(npc);
            if (canonicalNpc.type == NPCID.TargetDummy
                || canonicalNpc.ExcludedFromDeathTally()
                || !StableContentKey.TryCreateForNpc(canonicalNpc, out content))
            {
                canonicalNpc = null;
                return false;
            }

            lastInteraction = canonicalNpc.lastInteraction;
            return true;
        }

        /// <summary>
        /// 获取共享生命 NPC 的主体；无有效主体时保留死亡实例自身。
        /// </summary>
        private static NPC ResolveRealLifeRoot(NPC npc)
        {
            return npc.realLife >= 0 && npc.realLife < Main.npc.Length
                ? Main.npc[npc.realLife]
                : npc;
        }

        /// <summary>
        /// 判断当前死亡节段之外是否仍有存活的世界吞噬怪节段。
        /// </summary>
        private static bool HasAnotherActiveEaterOfWorldsSegment(NPC npc)
        {
            foreach (NPC candidate in Main.npc)
            {
                if (candidate.active
                    && candidate.whoAmI != npc.whoAmI
                    && IsEaterOfWorldsSegment(candidate.type))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断 NPC 是否属于没有统一 realLife 主体的世界吞噬怪节段。
        /// </summary>
        private static bool IsEaterOfWorldsSegment(int npcType)
        {
            return npcType == NPCID.EaterofWorldsHead
                || npcType == NPCID.EaterofWorldsBody
                || npcType == NPCID.EaterofWorldsTail;
        }

        /// <summary>
        /// 排除会独立触发死亡回调、但不应代表整场 Boss 击杀的原版附属部位。
        /// </summary>
        private static bool IsNonCanonicalVanillaPart(int npcType)
        {
            return npcType == NPCID.SkeletronHand
                || npcType == NPCID.PrimeCannon
                || npcType == NPCID.PrimeSaw
                || npcType == NPCID.PrimeVice
                || npcType == NPCID.PrimeLaser
                || npcType == NPCID.GolemHead
                || npcType == NPCID.GolemHeadFree
                || npcType == NPCID.GolemFistLeft
                || npcType == NPCID.GolemFistRight
                || npcType == NPCID.MartianSaucerTurret
                || npcType == NPCID.MartianSaucerCannon
                || npcType == NPCID.MoonLordHead
                || npcType == NPCID.MoonLordHand
                || npcType == NPCID.MoonLordFreeEye
                || npcType == NPCID.PirateShipCannon;
        }
    }
}
