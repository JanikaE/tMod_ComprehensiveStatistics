using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 标记原版快捷治疗、快捷魔力与快捷增益的执行作用域，供物品统计识别其实际使用。
    /// </summary>
    internal sealed class StatisticsQuickUseSystem : ModSystem
    {
        private static int[] quickUseDepthByPlayer;

        public override void Load()
        {
            quickUseDepthByPlayer = new int[Main.maxPlayers];
            On_Player.QuickHeal += HandleQuickHeal;
            On_Player.QuickMana += HandleQuickMana;
            On_Player.QuickBuff += HandleQuickBuff;
        }

        public override void Unload()
        {
            On_Player.QuickHeal -= HandleQuickHeal;
            On_Player.QuickMana -= HandleQuickMana;
            On_Player.QuickBuff -= HandleQuickBuff;
            quickUseDepthByPlayer = null;
        }

        /// <summary>
        /// 判断玩家当前是否正在执行一种原版快捷物品使用操作。
        /// </summary>
        internal static bool IsQuickUseActive(Player player)
        {
            return player != null
                && quickUseDepthByPlayer != null
                && player.whoAmI >= 0
                && player.whoAmI < quickUseDepthByPlayer.Length
                && quickUseDepthByPlayer[player.whoAmI] > 0;
        }

        /// <summary>
        /// 在快捷治疗调用期间建立统计作用域。
        /// </summary>
        private static void HandleQuickHeal(On_Player.orig_QuickHeal orig, Player player)
        {
            BeginQuickUse(player);
            try
            {
                orig(player);
            }
            finally
            {
                EndQuickUse(player);
            }
        }

        /// <summary>
        /// 在快捷魔力调用期间建立统计作用域。
        /// </summary>
        private static void HandleQuickMana(On_Player.orig_QuickMana orig, Player player)
        {
            BeginQuickUse(player);
            try
            {
                orig(player);
            }
            finally
            {
                EndQuickUse(player);
            }
        }

        /// <summary>
        /// 在快捷增益调用期间建立统计作用域。
        /// </summary>
        private static void HandleQuickBuff(On_Player.orig_QuickBuff orig, Player player)
        {
            BeginQuickUse(player);
            try
            {
                orig(player);
            }
            finally
            {
                EndQuickUse(player);
            }
        }

        /// <summary>
        /// 增加玩家的快捷使用作用域深度。
        /// </summary>
        private static void BeginQuickUse(Player player)
        {
            if (player != null
                && quickUseDepthByPlayer != null
                && player.whoAmI >= 0
                && player.whoAmI < quickUseDepthByPlayer.Length)
            {
                quickUseDepthByPlayer[player.whoAmI]++;
            }
        }

        /// <summary>
        /// 减少玩家的快捷使用作用域深度，并防止异常调用造成负值。
        /// </summary>
        private static void EndQuickUse(Player player)
        {
            if (player != null
                && quickUseDepthByPlayer != null
                && player.whoAmI >= 0
                && player.whoAmI < quickUseDepthByPlayer.Length
                && quickUseDepthByPlayer[player.whoAmI] > 0)
            {
                quickUseDepthByPlayer[player.whoAmI]--;
            }
        }
    }
}
