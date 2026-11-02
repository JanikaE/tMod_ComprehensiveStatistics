using System;
using System.Collections.Generic;
using ComprehensiveStatistics.Common.Players;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>在原版配方成功回调结束后记录产物，专用模组的四参数入口留给其兼容层处理。</summary>
    internal sealed class StatisticsCraftingSystem : ModSystem
    {
        private Hook craftHook;
        [ThreadStatic]
        private static List<(int Type, int Units)> pendingCrafts;

        public override void Load()
        {
            // 三参数入口只转发一次到四参数入口；后者也被魔法存储的模拟和待结算合成调用，不能直接入账。
            craftHook = new Hook(typeof(RecipeLoader).GetMethod(nameof(RecipeLoader.OnCraft),
                new[] { typeof(Item), typeof(Recipe), typeof(Item) }),
                (Action<Action<Item, Recipe, Item>, Item, Recipe, Item>)HandleCraft);
            // 叶子先安装，再重建原版调用方，避免预编译内联副本绕过采集。
            On_Main.CraftItem += HandleNativeCraft;
        }

        public override void Unload()
        {
            On_Main.CraftItem -= HandleNativeCraft;
            craftHook?.Dispose();
            craftHook = null;
            pendingCrafts = null;
        }

        /// <summary>在原版作用域内，于配方及物品的产物修改回调正常结束后读取本次新产物数量。</summary>
        private static void HandleCraft(Action<Item, Recipe, Item> orig, Item item, Recipe recipe, Item destination)
        {
            orig(item, recipe, destination);
            // item 是尚未与 destination 合并的新产物，不能取鼠标的最终总堆叠数。
            if (pendingCrafts != null && item != null && !item.IsAir)
                pendingCrafts.Add((item.type, item.stack));
        }

        /// <summary>固定原版操作的本地玩家，等待产物合并流程正常完成后提交不可变候选。</summary>
        private static void HandleNativeCraft(On_Main.orig_CraftItem orig, Recipe recipe)
        {
            Player player = Main.netMode != NetmodeID.Server && Main.myPlayer >= 0
                && Main.myPlayer < Main.maxPlayers ? Main.player[Main.myPlayer] : null;
            var previous = pendingCrafts;
            var current = new List<(int Type, int Units)>();
            pendingCrafts = current;
            try
            {
                orig(recipe);
                if (player != null && player.active)
                    foreach (var output in current)
                        player.GetModPlayer<StatisticsPlayer>().RecordItemCraft(output.Type, output.Units);
            }
            finally
            {
                // 嵌套操作各自结算；异常候选不会跨调用或跨世界延迟提交。
                pendingCrafts = previous;
            }
        }
    }
}
