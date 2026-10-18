using Terraria;
using Terraria.ID;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 将原版 NPC 的第一、第二对话按钮映射为稳定功能；未知或模组按钮保留位置兜底。
    /// </summary>
    internal static class NpcChatButtonClassifier
    {
        /// <summary>
        /// 根据 NPC 类型和按钮位置返回不会依赖本地化文本的功能分类。
        /// </summary>
        internal static NpcChatButtonKind Classify(NPC npc, bool firstButton)
        {
            // ModNPC 可以在运行时任意改变按钮文字和行为，公开回调又不提供按钮文本。
            // 在没有主动注册接口前只保存位置，不能把自定义功能臆测成商店或任务。
            if (npc?.ModNPC != null)
            {
                return firstButton
                    ? NpcChatButtonKind.CustomFirst
                    : NpcChatButtonKind.CustomSecond;
            }

            return firstButton
                ? ClassifyVanillaFirstButton(npc?.type ?? NPCID.None)
                : ClassifyVanillaSecondButton(npc?.type ?? NPCID.None);
        }

        /// <summary>
        /// 分类原版第一按钮；未列出的原版可点击第一按钮均为商店入口。
        /// </summary>
        private static NpcChatButtonKind ClassifyVanillaFirstButton(int npcType)
        {
            return npcType switch
            {
                NPCID.Angler => NpcChatButtonKind.QuestFirst,
                NPCID.Guide => NpcChatButtonKind.HelpFirst,
                NPCID.Nurse => NpcChatButtonKind.HealingFirst,
                NPCID.TaxCollector => NpcChatButtonKind.TaxCollectionFirst,
                NPCID.OldMan => NpcChatButtonKind.CurseFirst,
                _ => NpcChatButtonKind.ShopFirst
            };
        }

        /// <summary>
        /// 分类原版第二按钮；未知扩展保留为第二按钮兜底而不猜测功能。
        /// </summary>
        private static NpcChatButtonKind ClassifyVanillaSecondButton(int npcType)
        {
            return npcType switch
            {
                NPCID.Dryad => NpcChatButtonKind.WorldStatusOrPurifySecond,
                NPCID.Guide => NpcChatButtonKind.CraftingSecond,
                NPCID.GoblinTinkerer => NpcChatButtonKind.ReforgeSecond,
                NPCID.Stylist => NpcChatButtonKind.HairStyleSecond,
                NPCID.DyeTrader => NpcChatButtonKind.DyeTradeSecond,
                NPCID.PartyGirl => NpcChatButtonKind.MusicSecond,
                NPCID.DD2Bartender => NpcChatButtonKind.TavernkeepHelpSecond,
                NPCID.Painter => NpcChatButtonKind.ShopSecond,
                _ => NpcChatButtonKind.CustomSecond
            };
        }
    }
}
