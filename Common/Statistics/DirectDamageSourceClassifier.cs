using Terraria;
using Terraria.DataStructures;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 将一次最终直接伤害归入唯一主类别；只使用 OnHurt 当刻仍可靠的本地来源信息。
    /// </summary>
    internal static class DirectDamageSourceClassifier
    {
        /// <summary>
        /// 根据最终受伤信息将一次直接伤害归入互斥类别，并返回可识别的 NPC 来源。
        /// </summary>
        internal static DirectDamageSourceKind Classify(Player.HurtInfo info, out NPC npcSource)
        {
            npcSource = null;

            // PvP 必须先于实体判定：TryGetCausingEntity 对玩家弹幕可能返回玩家本身。
            if (info.PvP)
            {
                return DirectDamageSourceKind.PvP;
            }

            if (info.DamageSource.TryGetCausingEntity(out Entity causingEntity))
            {
                if (causingEntity is Projectile projectile)
                {
                    // 原版陷阱弹幕设置 trap；originatedFromActivableTile 为模组机关提供补充信号。
                    return projectile.trap || projectile.originatedFromActivableTile
                        ? DirectDamageSourceKind.TrapOrTile
                        : projectile.hostile
                            ? DirectDamageSourceKind.HostileProjectile
                            : DirectDamageSourceKind.Other;
                }

                if (causingEntity is NPC npc)
                {
                    npcSource = npc;
                    return DirectDamageSourceKind.NpcContact;
                }

                if (causingEntity is Player)
                {
                    return DirectDamageSourceKind.PvP;
                }
            }

            // 原版约定：0 摔落、1 溺水、2 熔岩、3/4 为物块接触或采掘相关伤害。
            return info.DamageSource.SourceOtherIndex switch
            {
                0 => DirectDamageSourceKind.Fall,
                1 => DirectDamageSourceKind.Drowning,
                2 => DirectDamageSourceKind.Lava,
                3 or 4 => DirectDamageSourceKind.TrapOrTile,
                _ => DirectDamageSourceKind.Other
            };
        }

        /// <summary>
        /// 尝试从死亡原因还原直接伤害类别及 NPC 来源。
        /// </summary>
        internal static bool TryClassifyDeathReason(
            PlayerDeathReason reason,
            bool pvp,
            out DirectDamageSourceKind source,
            out NPC npcSource)
        {
            npcSource = null;
            if (pvp || reason.SourcePlayerIndex >= 0)
            {
                source = DirectDamageSourceKind.PvP;
                return true;
            }

            if (reason.TryGetCausingEntity(out Entity causingEntity))
            {
                if (causingEntity is Projectile projectile)
                {
                    source = projectile.trap || projectile.originatedFromActivableTile
                        ? DirectDamageSourceKind.TrapOrTile
                        : projectile.hostile
                            ? DirectDamageSourceKind.HostileProjectile
                            : DirectDamageSourceKind.Other;
                    return true;
                }

                if (causingEntity is NPC npc)
                {
                    npcSource = npc;
                    source = DirectDamageSourceKind.NpcContact;
                    return true;
                }

                if (causingEntity is Player)
                {
                    source = DirectDamageSourceKind.PvP;
                    return true;
                }
            }

            if (reason.SourceNPCIndex >= 0)
            {
                source = DirectDamageSourceKind.NpcContact;
                return true;
            }

            if (reason.SourceProjectileType > 0)
            {
                source = DirectDamageSourceKind.Other;
                return true;
            }

            source = reason.SourceOtherIndex switch
            {
                0 => DirectDamageSourceKind.Fall,
                1 => DirectDamageSourceKind.Drowning,
                2 => DirectDamageSourceKind.Lava,
                3 or 4 => DirectDamageSourceKind.TrapOrTile,
                _ => DirectDamageSourceKind.Other
            };
            // 254/255 是 LegacyEmpty/LegacyDefault 的无来源哨兵值，不能当作直接伤害。
            return reason.SourceOtherIndex >= 0 && reason.SourceOtherIndex < 254;
        }
    }
}
