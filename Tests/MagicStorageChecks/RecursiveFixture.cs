using System;
using System.Collections.Generic;
using Terraria;

namespace MagicStorage.Common.Systems.RecurrentRecipes
{
    /// <summary>代表节点计算的模拟材料环境。</summary>
    public sealed class AvailableRecipeObjects { }

    /// <summary>携带测试节点和材料可用性，不查询真实游戏配方。</summary>
    public sealed class Iteration
    {
        public readonly List<Node> Nodes = new();
        public bool Accepted = true;
        public int Amount = 1;
    }

    /// <summary>规定一个节点实际新生产的配方批次数及附加产物。</summary>
    public sealed class Node
    {
        public int Type, PerBatch, Batches = 1;
        public readonly List<Item> Extras = new();
    }

    /// <summary>保留对应发行版的值类型结果及合并入口。</summary>
    public struct CraftResult
    {
        public int Amount;
        /// <summary>形成被接受迭代的唯一合并边界。</summary>
        public CraftResult CombineWith(in CraftResult other) => new() { Amount = Amount + other.Amount };
    }

    /// <summary>缓存最终交付数量，允许测试修改它以验证快照隔离。</summary>
    public struct ExcessItemInfo
    {
        public int type;
        public int Stack { get; set; }
    }

    /// <summary>在每个节点计算生产量后模拟下游消耗，保留与上游相同的采集 IL 形状。</summary>
    public sealed class OrderedRecipeTree
    {
        public Iteration Iteration;
        public static int Observed;
        /// <summary>只计算生产节点；材料不足轮会由上层丢弃，不在这里提交统计。</summary>
        public void GetCraftingInformation(AvailableRecipeObjects available, out CraftResult result)
        {
            foreach (Node node in Iteration.Nodes)
            {
                int batches = node.Batches;
                Item createItem = new Item { type = node.Type, stack = node.PerBatch };
                var droppedItems = new List<Item>();
                droppedItems.AddRange(node.Extras);
                createItem.stack *= batches;
                Observed += createItem.stack + droppedItems.Count + batches;
                createItem.stack = 0;
            }
            result = new CraftResult { Amount = Iteration.Amount };
        }
    }

    /// <summary>每轮提供已剪枝的实际节点，已有库存覆盖的节点不会进入本列表。</summary>
    public sealed class RecursiveRecipe
    {
        public readonly List<Iteration> Iterations = new();
        public readonly List<ExcessItemInfo> Excess = new();
        public int Index;
        /// <summary>把当前轮交给与上游同签名的树计算入口。</summary>
        public void GetCraftingInformation(int amount, out CraftResult result, AvailableRecipeObjects available, int blocked)
            => new OrderedRecipeTree { Iteration = Iterations[Index] }.GetCraftingInformation(available, out result);
    }

    /// <summary>模拟已接受迭代和失败末轮，缓存对象后续原样交给真实递归步骤。</summary>
    public sealed class CraftingSimulation
    {
        private CraftResult simulationResult;
        public int AmountCrafted { get; set; }
        public IReadOnlyList<ExcessItemInfo> ExcessResults { get; private set; }

        /// <summary>模拟时只保存计划，不提交统计。</summary>
        public void SimulateCrafts(RecursiveRecipe recipe, int amount, AvailableRecipeObjects available)
        {
            bool previous = CraftingGUI.SimulatingCrafts;
            CraftingGUI.SimulatingCrafts = true;
            try { SimulateCrafts_Inner(recipe, amount, available); }
            finally { CraftingGUI.SimulatingCrafts = previous; }
        }

        /// <summary>只有验证成功的轮才通过 CombineWith 写入已接受结果。</summary>
        private void SimulateCrafts_Inner(RecursiveRecipe recipe, int amount, AvailableRecipeObjects available)
        {
            simulationResult = default;
            for (recipe.Index = 0; recipe.Index < recipe.Iterations.Count; recipe.Index++)
            {
                recipe.GetCraftingInformation(1, out CraftResult result, available, 0);
                if (!recipe.Iterations[recipe.Index].Accepted) break;
                simulationResult = simulationResult.CombineWith(result);
            }
            AmountCrafted = simulationResult.Amount;
            ExcessResults = recipe.Excess;
        }
    }
}
