using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 使用模组名与内容内部名标识内容，避免存档依赖会随加载顺序变化的运行时数字 ID。
    /// </summary>
    internal readonly struct StableContentKey : IEquatable<StableContentKey>
    {
        /// <summary>
        /// 创建一个不依赖运行时数字 ID 的内容键。
        /// </summary>
        internal StableContentKey(string modName, string contentName)
        {
            if (string.IsNullOrWhiteSpace(modName))
            {
                throw new ArgumentException("Mod name cannot be empty.", nameof(modName));
            }

            if (string.IsNullOrWhiteSpace(contentName))
            {
                throw new ArgumentException("Content name cannot be empty.", nameof(contentName));
            }

            ModName = modName;
            ContentName = contentName;
        }

        internal string ModName { get; }

        internal string ContentName { get; }

        /// <summary>
        /// 尝试从 NPC 实例创建稳定内容键。
        /// </summary>
        internal static bool TryCreateForNpc(NPC npc, out StableContentKey key)
        {
            if (npc?.ModNPC != null)
            {
                key = new StableContentKey(npc.ModNPC.Mod.Name, npc.ModNPC.Name);
                return true;
            }

            if (npc != null && NPCID.Search.TryGetName(npc.type, out string contentName))
            {
                key = new StableContentKey("Terraria", contentName);
                return true;
            }

            key = default;
            return false;
        }

        /// <summary>
        /// 尝试从物品类型创建稳定内容键。
        /// </summary>
        internal static bool TryCreateForItem(int itemType, out StableContentKey key)
        {
            ModItem modItem = ItemLoader.GetItem(itemType);
            if (modItem != null)
            {
                key = new StableContentKey(modItem.Mod.Name, modItem.Name);
                return true;
            }

            if (ItemID.Search.TryGetName(itemType, out string contentName))
            {
                key = new StableContentKey("Terraria", contentName);
                return true;
            }

            key = default;
            return false;
        }

        /// <summary>
        /// 尝试从 Buff 类型创建稳定内容键。
        /// </summary>
        internal static bool TryCreateForBuff(int buffType, out StableContentKey key)
        {
            ModBuff modBuff = BuffLoader.GetBuff(buffType);
            if (modBuff != null)
            {
                key = new StableContentKey(modBuff.Mod.Name, modBuff.Name);
                return true;
            }

            if (BuffID.Search.TryGetName(buffType, out string contentName))
            {
                key = new StableContentKey("Terraria", contentName);
                return true;
            }

            key = default;
            return false;
        }

        /// <summary>
        /// 尝试从持久化的“模组名/内容内部名”文本恢复稳定内容键。
        /// </summary>
        internal static bool TryParse(string value, out StableContentKey key)
        {
            int separatorIndex = value?.IndexOf('/') ?? -1;
            if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
            {
                key = default;
                return false;
            }

            key = new StableContentKey(
                value.Substring(0, separatorIndex),
                value.Substring(separatorIndex + 1));
            return true;
        }

        /// <summary>
        /// 尝试将稳定内容键解析为当前会话中的 NPC 类型 ID。
        /// </summary>
        internal bool TryResolveNpcType(out int npcType)
        {
            if (string.Equals(ModName, "Terraria", StringComparison.Ordinal))
            {
                return NPCID.Search.TryGetId(ContentName, out npcType);
            }

            if (ModContent.TryFind(ModName, ContentName, out ModNPC modNpc))
            {
                npcType = modNpc.Type;
                return true;
            }

            npcType = 0;
            return false;
        }

        /// <summary>
        /// 尝试将稳定内容键解析为当前会话中的物品类型 ID。
        /// </summary>
        internal bool TryResolveItemType(out int itemType)
        {
            if (string.Equals(ModName, "Terraria", StringComparison.Ordinal))
            {
                return ItemID.Search.TryGetId(ContentName, out itemType);
            }

            if (ModContent.TryFind(ModName, ContentName, out ModItem modItem))
            {
                itemType = modItem.Type;
                return true;
            }

            itemType = 0;
            return false;
        }

        /// <summary>
        /// 尝试将稳定内容键解析为当前会话中的 Buff 类型 ID。
        /// </summary>
        internal bool TryResolveBuffType(out int buffType)
        {
            if (string.Equals(ModName, "Terraria", StringComparison.Ordinal))
            {
                return BuffID.Search.TryGetId(ContentName, out buffType);
            }

            if (ModContent.TryFind(ModName, ContentName, out ModBuff modBuff))
            {
                buffType = modBuff.Type;
                return true;
            }

            buffType = 0;
            return false;
        }

        /// <summary>
        /// 按模组名和内容内部名比较两个稳定内容键。
        /// </summary>
        public bool Equals(StableContentKey other)
        {
            return string.Equals(ModName, other.ModName, StringComparison.Ordinal)
                && string.Equals(ContentName, other.ContentName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is StableContentKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                ModName == null ? 0 : StringComparer.Ordinal.GetHashCode(ModName),
                ContentName == null ? 0 : StringComparer.Ordinal.GetHashCode(ContentName));
        }

        public override string ToString()
        {
            return $"{ModName}/{ContentName}";
        }

        /// <summary>
        /// 判断两个稳定内容键是否相等。
        /// </summary>
        public static bool operator ==(StableContentKey left, StableContentKey right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// 判断两个稳定内容键是否不相等。
        /// </summary>
        public static bool operator !=(StableContentKey left, StableContentKey right)
        {
            return !left.Equals(right);
        }
    }
}
