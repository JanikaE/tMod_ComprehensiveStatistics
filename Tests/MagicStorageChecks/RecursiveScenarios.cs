using System;
using System.Collections.Generic;
using System.Reflection;
using MagicStorage.Common.Systems.RecurrentRecipes;
using Terraria;
using GUI = MagicStorage.CraftingGUI;

/// <summary>经过真实采集和迭代 IL 边界验证全链产量，库存剪枝由测试输入明确指定。</summary>
internal static class RecursiveScenarios
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static int checks;

    /// <summary>复用普通场景已安装的游戏回调替身，仅把递归计算与实际交付串入同一生产事务。</summary>
    internal static void Run(Type collector, List<IDisposable> hooks, Action<Action<int>, int, bool, Action<int, long>> execute)
    {
        collector.GetMethod("InstallRecursionHooks", Flags).Invoke(null, new object[] { typeof(GUI).Assembly, hooks });
        var recorded = new Dictionary<int, long>();
        void Credit(int type, long units) { recorded.TryGetValue(type, out long old); recorded[type] = old + units; }
        long Quantity(int type) => recorded.GetValueOrDefault(type);
        void Reset()
        {
            recorded.Clear(); GUI.UseRecursion = true; GUI.StopRecursion = GUI.KeepLeftovers = false;
            GUI.AcceptMaterials = true; GUI.ModifyCallback = null;
            GUI.Simulation = new CraftingSimulation();
        }
        RecursiveRecipe Plan(params (int Type, int Units)[] nodes)
        {
            var recipe = new RecursiveRecipe();
            var iteration = new Iteration();
            foreach (var node in nodes) iteration.Nodes.Add(new Node { Type = node.Type, PerBatch = node.Units });
            recipe.Iterations.Add(iteration);
            recipe.Excess.Add(new ExcessItemInfo { type = 100, Stack = 1 });
            return recipe;
        }
        void Preview(RecursiveRecipe recipe) => GUI.Simulation.SimulateCrafts(recipe, 100, new AvailableRecipeObjects());
        void Craft(bool eligible = true) => execute(GUI.Craft, 100, eligible, Credit);

        Reset(); Preview(Plan((10, 8), (100, 1))); Check(recorded.Count == 0, "preview does not commit");
        Craft(); Check(Quantity(10) == 8 && Quantity(100) == 1, "consumed intermediate and final item both count");
        Reset(); Preview(Plan((100, 1))); Craft(); Check(Quantity(10) == 0 && Quantity(100) == 1, "stock-satisfied intermediate has no production node");
        Reset(); Preview(Plan((10, 6), (100, 1))); Craft(); Check(Quantity(10) == 6, "partial stock only newly produced shortage counts");
        Reset(); var excess = Plan((10, 10), (100, 1)); excess.Excess.Add(new ExcessItemInfo { type = 10, Stack = 2 });
        Preview(excess); Craft(); Check(Quantity(10) == 10 && Quantity(100) == 1, "eight consumed plus two delivered equals ten, no extra remainder count");
        Reset(); Preview(Plan((10, 2), (10, 4), (100, 1))); Craft(); Check(Quantity(10) == 6, "same-type nodes in multiple branches sum");
        Reset(); var rejected = Plan((10, 3), (100, 1)); var failed = new Iteration { Accepted = false };
        failed.Nodes.Add(new Node { Type = 10, PerBatch = 999 }); rejected.Iterations.Add(failed);
        Preview(rejected); Craft(); Check(Quantity(10) == 3, "rejected last simulation iteration discarded");
        Reset(); var batch = Plan((10, 2), (100, 1)); var next = new Iteration();
        next.Nodes.Add(new Node { Type = 10, PerBatch = 3 }); next.Nodes.Add(new Node { Type = 100, PerBatch = 1 });
        batch.Iterations.Add(next); batch.Excess[0] = new ExcessItemInfo { type = 100, Stack = 2 };
        Preview(batch); Craft(); Check(Quantity(10) == 5 && Quantity(100) == 2, "accepted iterations accumulate actual production");
        Reset(); var rounded = Plan((100, 3)); rounded.Excess[0] = new ExcessItemInfo { type = 100, Stack = 3 };
        Preview(rounded); Craft(); Check(Quantity(100) == 3, "output batch exceeds target and unused output is fully counted");
        Reset(); var extra = Plan((10, 8), (100, 1)); extra.Iterations[0].Nodes[0].Extras.Add(new Item { type = 11, stack = 4 });
        extra.Excess.Add(new ExcessItemInfo { type = 11, Stack = 1 }); Preview(extra); Craft();
        Check(Quantity(11) == 4, "extra node output consumed downstream plus final excess both count once");
        Reset(); Preview(Plan((10, 8), (100, 1))); GUI.ModifyCallback = item => { if (item.type == 100) item.stack = 2; };
        Craft(); Check(Quantity(10) == 8 && Quantity(100) == 2, "post-callback final quantity replaces simulated excess only");
        Reset(); Preview(Plan((10, 8), (100, 1))); GUI.ModifyCallback = item => { if (item.type == 100) item.type = 101; };
        Craft(); Check(Quantity(10) == 8 && Quantity(100) == 0 && Quantity(101) == 1, "post-callback final type respected without changing consumed intermediate");
        Reset(); Preview(Plan((10, 8), (100, 1))); GUI.AcceptMaterials = false; Craft(); Check(recorded.Count == 0, "material rollback cancels entire chain");
        Reset(); Preview(Plan((10, 8), (100, 1))); GUI.StopRecursion = true; Craft(); Check(recorded.Count == 0, "failed recursive context does not commit cached plan");
        Reset(); Preview(Plan((10, 8), (100, 1))); GUI.KeepLeftovers = true; Craft(); Check(Quantity(10) == 8 && Quantity(100) == 1, "full storage does not duplicate successful chain");
        Reset(); Preview(Plan((10, 8), (100, 1))); Craft(false); Check(recorded.Count == 0, "multiplayer remains excluded");
        Reset(); Preview(Plan((10, 8), (100, 1))); GUI.Simulation.AmountCrafted++;
        Craft(); Check(recorded.Count == 0, "changed simulation amount invalidates plan");
        Reset(); Preview(Plan((10, 8), (100, 1))); collector.GetMethod("ResetWorld", Flags).Invoke(null, null);
        Craft(); Check(recorded.Count == 0, "old-world cached plan cannot commit");
        Reset(); Preview(Plan((10, 8), (100, 1))); Preview(Plan((10, 2), (100, 1)));
        Craft(); Check(Quantity(10) == 2, "recalculation of same cache object replaces old candidate");
        Reset(); Preview(Plan((10, 8), (100, 1))); try { execute(amount => { GUI.Craft(amount); throw new InvalidOperationException("fixture"); }, 100, true, Credit); }
        catch (InvalidOperationException) { }
        Check(recorded.Count == 0, "exception after delivery does not commit pending chain");
        Reset(); var invalid = Plan((10, 1), (100, 1)); invalid.Excess.Add(new ExcessItemInfo { type = 10, Stack = 2 });
        Preview(invalid); Craft(); Check(recorded.Count == 0, "inconsistent excess never falls back to final-only count");
        Reset(); var multiples = Plan((100, 1)); multiples.Iterations[0].Nodes.Insert(0, new Node { Type = 10, PerBatch = 3, Batches = 4 });
        Preview(multiples); Craft(); Check(Quantity(10) == 12, "node output includes all ingredient batches");
        Reset(); Preview(Plan((10, 8), (100, 1))); Craft(); Craft(); Check(Quantity(10) == 16 && Quantity(100) == 2, "two actual settlements of same plan each count once");
        Console.WriteLine($"MAGIC_STORAGE_RECURSIVE_SCENARIOS_OK: {checks} accepted-chain, stock-pruned nodes, branch sums, preview, rollback and cache checks; isolated fixtures");
    }

    /// <summary>验证一个递归数量或事务不变量。</summary>
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
}
