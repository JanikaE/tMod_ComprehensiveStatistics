using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ModLoader;
using GUI = MagicStorage.CraftingGUI;

/// <summary>使用真实生产适配器和 MonoMod Hook 验证单人合成事务，不启动游戏或访问存档。</summary>
internal static class Scenarios
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static int checks;

    /// <summary>覆盖批量、回退、模拟、递归排除、去重、世界失效和普通合成结果。</summary>
    internal static void Run(Assembly production)
    {
        Type collector = production.GetType("ComprehensiveStatistics.Common.Systems.MagicStorageCraftingSystem", true);
        var hooks = new List<IDisposable>();
        MethodInfo callback = typeof(RecipeLoader).GetMethod("OnCraft", new[] { typeof(Item), typeof(Recipe), typeof(List<Item>), typeof(Item) });
        using var gameCallback = new Hook(callback, (Action<Action<Item, Recipe, List<Item>, Item>, Item, Recipe, List<Item>, Item>)
            ((orig, item, recipe, consumed, destination) => GUI.ModifyCallback?.Invoke(item)));
        try
        {
            foreach (Type nested in typeof(GUI).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (MethodInfo method in nested.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (method.Name.StartsWith("<Craft>b__", StringComparison.Ordinal)) RuntimeHelpers.PrepareMethod(method.MethodHandle);
            collector.GetMethod("InstallHooks", Flags).Invoke(null, new object[] { typeof(GUI).Assembly, hooks, false });
            Action<int> runStandard = GUI.Craft;
            var execute = (Action<Action<int>, int, bool, Action<int, long>>)collector.GetMethod("ExecuteCraft", Flags)
                .CreateDelegate(typeof(Action<Action<int>, int, bool, Action<int, long>>));
            var recorded = new List<(int Type, long Units)>();
            Action<int, long> credit = (type, units) => recorded.Add((type, units));
            void Reset()
            {
                recorded.Clear(); GUI.AcceptMaterials = true; GUI.KeepLeftovers = GUI.UseRepeated = GUI.UseRecursion = GUI.StopRecursion = false;
                GUI.SimulatingCrafts = false; GUI.ModifyCallback = null;
                GUI.Produce = context => { context.toWithdraw.Add(new Item { type = 9, stack = 1 }); GUI.Output(context, 8, 3); };
            }
            void Run(bool eligible = true) => execute(runStandard, 100, eligible, credit);
            long Units() => recorded.Sum(output => output.Units);

            Reset(); Run(); Check(Units() == 3 && recorded.Count == 1, "single craft, exact output");
            Reset(); GUI.UseRepeated = true; Run(); Check(Units() == 3, "batch fallback to repeated single craft");
            Reset(); GUI.Produce = context => { for (int i = 0; i < 10; i++) GUI.Output(context, 8, 3); };
            Run(); Check(Units() == 30, "batch counts each actual output, not requested target");
            Reset(); GUI.ModifyCallback = item => item.stack = 6;
            Run(); Check(Units() == 6, "post-callback quantity survives deposit mutation");
            Reset(); GUI.Produce = context => { Item item = GUI.Output(context, 8, 3); RecipeLoader.OnCraft(item, null, new List<Item>(), new Item()); };
            Run(); Check(Units() == 3, "same output callback deduplicated");
            Reset(); GUI.AcceptMaterials = false; Run(); Check(Units() == 0, "withdrawal rollback never passes accepted boundary");
            GUI.AcceptMaterials = true; Run(); Check(Units() == 3, "failure does not poison later success");
            Reset(); GUI.KeepLeftovers = true; Run(); Check(Units() == 3, "storage full leftovers counted after normal completion");
            Reset(); GUI.Produce = context => GUI.Output(context, 8, 3);
            Run(); Check(Units() == 3, "zero withdrawal or saved ingredients still produce output");
            Reset(); GUI.Produce = context => { var first = GUI.Output(context, 8, 3); var second = GUI.Output(context, 8, 3); first.stack = 6; second.stack = 0; context.results = new List<Item> { first }; };
            Run(); Check(Units() == 6, "compaction retains original produced quantities exactly once");
            Reset(); GUI.SimulatingCrafts = true; Run(); Check(Units() == 0, "global simulation flag excluded");
            Reset(); GUI.Produce = context => { context.simulation = true; GUI.Output(context, 8, 3); context.simulation = false; };
            Run(); Check(Units() == 0, "context simulation excluded");
            Reset(); execute(amount => { var context = new GUI.CraftingContext(); GUI.Output(context, 8, 3); GUI.HandleCraftWithdrawAndDeposit(new(), context.toWithdraw, context.results); }, 1, true, credit);
            Check(Units() == 0, "recursive or preview path without ordinary scope excluded");
            Reset(); Run(false); Check(Units() == 0, "multiplayer or invalid player gate excluded");
            Reset(); execute(amount => { var context = new GUI.CraftingContext(); GUI.Craft_DoStandardCraft(context); GUI.HandleCraftWithdrawAndDeposit(new(), new List<Item>(), context.results); }, 1, true, credit);
            Check(Units() == 0, "unrelated settlement cannot confirm ordinary context");
            Reset(); try { execute(amount => { runStandard(amount); throw new InvalidOperationException("fixture"); }, 1, true, credit); }
            catch (InvalidOperationException) { }
            Check(Units() == 0, "root exception does not commit pending candidates");
            Reset(); GUI.Produce = context => { GUI.Output(context, 8, 3); GUI.Output(context, 8, 3); };
            Run(); Check(Units() == 6, "partial target success counts six, not requested hundred");
            Reset(); GUI.Produce = context => { GUI.Output(context, 8, 3); RecipeLoader.OnCraft(new Item { type = 9, stack = 99 }, null, new List<Item>(), new Item()); };
            Run(); Check(Units() == 3, "unrelated nested item callback excluded");
            Reset(); execute(amount => { runStandard(amount); collector.GetMethod("ResetWorld", Flags).Invoke(null, null); }, 1, true, credit);
            Check(Units() == 0, "world change invalidates pending success");
            Reset(); Run(); Check(Units() == 3, "new world operation recovers");
            Reset(); execute(amount => { execute(runStandard, 1, false, credit); runStandard(amount); }, 1, true, credit);
            Check(Units() == 3, "nested ineligible operation isolated and outer context restored");
            Reset(); execute(amount =>
            {
                object operation = collector.GetField("active", Flags).GetValue(null);
                operation.GetType().GetField("Networked", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(operation, true);
                runStandard(amount);
            }, 1, true, credit);
            Check(Units() == 0, "client cannot fall back to local settlement without server confirmation");
            Console.WriteLine($"MAGIC_STORAGE_SCENARIOS_OK: {checks} actual-hook checks; isolated fixtures, not in-game validation");
            RecursiveScenarios.Run(collector, hooks, execute);
            NetworkScenarios.Run(collector, hooks);
        }
        finally { collector.GetMethod("DisposeHooks", Flags).Invoke(null, new object[] { hooks }); }
    }

    /// <summary>断言一个事务不变量。</summary>
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
}
