using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Terraria;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>捕获上游递归计算中被接受的生产节点，绑定原模拟对象，在同次实际结算后提交全链数量。</summary>
    internal sealed partial class MagicStorageCraftingSystem
    {
        private static ConditionalWeakTable<object, ProductionPlan> plans = new();
        private static readonly object planSync = new();
        private static PropertyInfo amountProperty, excessProperty, excessStackProperty;
        private static FieldInfo excessTypeField;
        [ThreadStatic] private static SimulationCapture capture;

        /// <summary>独立安装递归能力；任何签名或唯一边界变化只关闭本组，保留普通合成。</summary>
        internal static void InstallRecursionHooks(Assembly code, List<IDisposable> installed)
        {
            const string prefix = "MagicStorage.Common.Systems.RecurrentRecipes.";
            Type gui = code.GetType("MagicStorage.CraftingGUI", true);
            Type context = gui.GetNestedType("CraftingContext", BindingFlags.Public | BindingFlags.NonPublic);
            Type simulation = code.GetType(prefix + "CraftingSimulation", true);
            Type recursive = code.GetType(prefix + "RecursiveRecipe", true);
            Type available = code.GetType(prefix + "AvailableRecipeObjects", true);
            Type tree = code.GetType(prefix + "OrderedRecipeTree", true);
            Type result = code.GetType(prefix + "CraftResult", true);
            Type excess = code.GetType(prefix + "ExcessItemInfo", true);
            amountProperty = RequireProperty(simulation, "AmountCrafted", typeof(int));
            excessProperty = RequireProperty(simulation, "ExcessResults", typeof(IReadOnlyList<>).MakeGenericType(excess));
            excessStackProperty = RequireProperty(excess, "Stack", typeof(int));
            excessTypeField = RequireField(excess, "type", typeof(int));
            MethodInfo node = RequireInstance(tree, "GetCraftingInformation", typeof(void), available, result.MakeByRefType());
            MethodInfo inner = RequireInstance(simulation, "SimulateCrafts_Inner", typeof(void), recursive, typeof(int), available);
            MethodInfo simulate = RequireInstance(simulation, "SimulateCrafts", typeof(void), recursive, typeof(int), available);
            MethodInfo step = Require(gui, "Craft_DoRecursionCraft", typeof(void), context);
            installed.Add(new ILHook(node, InstallProductionBoundary));
            installed.Add(new ILHook(inner, InstallIterationBoundaries));
            installed.Add(new Hook(simulate, (Action<Action<object, object, int, object>, object, object, int, object>)HandleSimulation));
            installed.Add(new ILHook(step, InstallPlanBinding));
            installed.Add(new Hook(step, (Action<Action<object>, object>)HandleRecursion));
            foreach (Type nested in gui.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (MethodInfo caller in nested.GetMethods(Methods))
                    if (caller.Name.StartsWith("<Craft_WithRecursion>b__", StringComparison.Ordinal) && caller.GetMethodBody() != null)
                        installed.Add(new ILHook(caller, RecompileCaller));
            installed.Add(new ILHook(Require(gui, "Craft_WithRecursion", context, typeof(int)), RecompileCaller));
            installed.Add(new ILHook(Require(gui, "Craft", typeof(void), typeof(int)), RecompileCaller));
        }

        /// <summary>在每个节点的新产物乘以批次后、共享余量计数器被下游消耗之前采集标量。</summary>
        private static void InstallProductionBoundary(ILContext il)
        {
            var stores = il.Body.Instructions.Where(instruction => instruction.OpCode.Code == Code.Stfld
                && instruction.Operand is FieldReference field && field.DeclaringType.FullName == "Terraria.Item"
                && field.Name == "stack" && instruction.Previous?.OpCode.Code == Code.Mul).ToArray();
            var lists = il.Body.Instructions.Where(instruction => instruction.OpCode.Code == Code.Newobj
                && instruction.Operand is MethodReference method && method.DeclaringType.FullName == "System.Collections.Generic.List`1<Terraria.Item>"
                && instruction.Next.MatchStloc(out _)).ToArray();
            if (stores.Length != 1 || lists.Length != 1
                || !stores[0].Previous.Previous.Previous.Previous.Previous.MatchLdloc(out int itemLocal)
                || !lists[0].Next.MatchStloc(out int extrasLocal)
                || il.Body.Variables[itemLocal].VariableType.FullName != "Terraria.Item")
                throw new InvalidOperationException($"Unique MagicStorage recursive production boundary not found (stores={stores.Length}, lists={lists.Length}, load={stores.FirstOrDefault()?.Previous?.Previous?.Previous?.Previous?.Previous}).");
            var cursor = new ILCursor(il);
            cursor.Goto(stores[0], MoveType.After);
            cursor.Emit(OpCodes.Ldloc, itemLocal);
            cursor.Emit(OpCodes.Ldloc, extrasLocal);
            cursor.EmitDelegate<Action<Item, List<Item>>>(CaptureProduction);
        }

        /// <summary>每轮先清候选，只有材料验证通过并合并到正式模拟结果的迭代才保留。</summary>
        private static void InstallIterationBoundaries(ILContext il)
        {
            var begin = il.Body.Instructions.Where(instruction => instruction.Operand is MethodReference method
                && method.Name == "GetCraftingInformation" && method.DeclaringType.Name == "RecursiveRecipe").ToArray();
            var accepted = il.Body.Instructions.Where(instruction => instruction.Operand is MethodReference method
                && method.Name == "CombineWith" && method.DeclaringType.Name == "CraftResult"
                && instruction.Next?.OpCode.Code == Code.Stfld).ToArray();
            if (begin.Length != 1 || accepted.Length != 1) throw new InvalidOperationException("Unique MagicStorage simulation iteration boundaries not found.");
            var cursor = new ILCursor(il);
            cursor.Goto(begin[0], MoveType.Before);
            cursor.EmitDelegate<Action>(BeginIteration);
            cursor.Goto(accepted[0].Next, MoveType.After);
            cursor.EmitDelegate<Action>(AcceptIteration);
        }

        /// <summary>原递归步骤取得所用缓存对象后立即绑定，不重算配方或依据所选数量猜测。</summary>
        private static void InstallPlanBinding(ILContext il)
        {
            var calls = il.Body.Instructions.Where(instruction => instruction.Operand is MethodReference method
                && method.DeclaringType.FullName == "MagicStorage.CraftingGUI" && method.Name == "GetCraftingSimulationForCurrentRecipe").ToArray();
            if (calls.Length != 1) throw new InvalidOperationException("Unique MagicStorage recursive simulation binding not found.");
            var cursor = new ILCursor(il);
            cursor.Goto(calls[0], MoveType.After);
            cursor.Emit(OpCodes.Dup);
            cursor.EmitDelegate<Action<object>>(BindPlan);
        }

        /// <summary>计算只生成与缓存对象绑定的不可变计划，列表刷新和模拟结束本身均不入账。</summary>
        private static void HandleSimulation(Action<object, object, int, object> orig, object self, object recipe, int amount, object available)
        {
            var previous = capture;
            var current = new SimulationCapture(worldGeneration);
            capture = current;
            lock (planSync) plans.Remove(self);
            try
            {
                orig(self, recipe, amount, available);
                var consumed = new Dictionary<int, long>(current.Total);
                bool valid = current.Iterations > 0 && current.Generation == worldGeneration;
                foreach (object excess in (IEnumerable)excessProperty.GetValue(self))
                {
                    int type = (int)excessTypeField.GetValue(excess), units = (int)excessStackProperty.GetValue(excess);
                    if (units <= 0) continue;
                    if (!consumed.TryGetValue(type, out long gross) || gross < units) { valid = false; break; }
                    consumed[type] = gross - units;
                }
                int actual = (int)amountProperty.GetValue(self);
                if (valid && actual > 0)
                    lock (planSync)
                    {
                        plans.Remove(self);
                        plans.Add(self, new ProductionPlan(current.Generation, actual, consumed));
                    }
            }
            finally { capture = previous; }
        }

        /// <summary>递归候选仅在真实步骤作用域中采集，末尾产物回调只补交付数量，不重复全链计数。</summary>
        private static void HandleRecursion(Action<object> orig, object context)
        {
            Operation operation = active;
            if (operation == null) { orig(context); return; }
            object previous = operation.Context;
            operation.Recursive = true;
            operation.Context = context;
            try { orig(context); }
            finally { operation.Context = previous; }
        }

        /// <summary>仅认当前世界和原模拟数量均一致的计划，缺失计划时不降级成余量统计。</summary>
        private static void BindPlan(object simulation)
        {
            Operation operation = active;
            if (operation == null || !operation.Recursive || operation.Context == null) return;
            operation.Plan = null;
            if (simulation != null && plans.TryGetValue(simulation, out ProductionPlan plan)
                && plan.Generation == worldGeneration && plan.Amount == (int)amountProperty.GetValue(simulation)) operation.Plan = plan;
        }

        /// <summary>丢弃未通过上游材料验证的上一轮候选。</summary>
        private static void BeginIteration() { if (capture != null) capture.Iteration = new Dictionary<int, long>(); }

        /// <summary>记录新生产的主产物与模拟确认的附加产物，随后被使用也不扣除生产量。</summary>
        private static void CaptureProduction(Item output, List<Item> extras)
        {
            if (capture?.Iteration == null) return;
            if (output != null && !output.IsAir) AddQuantity(capture.Iteration, output.type, output.stack);
            foreach (Item item in extras) if (item != null && !item.IsAir) AddQuantity(capture.Iteration, item.type, item.stack);
        }

        /// <summary>只在上游将该轮结果纳入最终模拟后累加生产数量。</summary>
        private static void AcceptIteration()
        {
            if (capture?.Iteration == null) return;
            foreach (var output in capture.Iteration) AddQuantity(capture.Total, output.Key, output.Value);
            capture.Iteration = null;
            capture.Iterations++;
        }

        /// <summary>按类型饱和累加临时候选量，不持久化运行时 ID。</summary>
        private static void AddQuantity(Dictionary<int, long> totals, int type, long units)
        {
            if (type <= 0 || units <= 0) return;
            totals.TryGetValue(type, out long old);
            totals[type] = old > long.MaxValue - units ? long.MaxValue : old + units;
        }

        /// <summary>递归反射入口必须为具有方法体的实例方法。</summary>
        private static MethodInfo RequireInstance(Type type, string name, Type returns, params Type[] args)
        {
            MethodInfo method = type.GetMethod(name, Methods, null, args, null);
            if (method == null || method.IsStatic || method.ReturnType != returns || method.ContainsGenericParameters || method.GetMethodBody() == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        /// <summary>校验仅读取的缓存属性类型。</summary>
        private static PropertyInfo RequireProperty(Type type, string name, Type expected)
        {
            PropertyInfo property = type.GetProperty(name, Methods);
            if (property?.PropertyType != expected || property.GetMethod == null || property.GetMethod.IsStatic)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        /// <summary>世界切换不允许复用旧世界的模拟计划。</summary>
        private static void ResetRecursion() { plans = new ConditionalWeakTable<object, ProductionPlan>(); capture = null; }

        /// <summary>内容卸载或递归安装回滚时释放上游程序集引用。</summary>
        private static void ClearRecursionBindings()
        { amountProperty = excessProperty = excessStackProperty = null; excessTypeField = null; ResetRecursion(); }

        /// <summary>一轮模拟的线程独立采集状态，失败末轮不会纳入总量。</summary>
        private sealed class SimulationCapture
        {
            /// <summary>固定世界代次。</summary>
            internal SimulationCapture(int generation) { Generation = generation; }
            internal readonly int Generation;
            internal readonly Dictionary<int, long> Total = new();
            internal Dictionary<int, long> Iteration;
            internal int Iterations;
        }

        /// <summary>缓存已经被链内消耗的新生产数量，交付产物数量留给真实后置回调决定。</summary>
        private sealed class ProductionPlan
        {
            /// <summary>复制标量计划，避免上游共享计数器后续修改污染统计。</summary>
            internal ProductionPlan(int generation, int amount, Dictionary<int, long> consumed)
            { Generation = generation; Amount = amount; ConsumedProduction = new Dictionary<int, long>(consumed); }
            internal readonly int Generation, Amount;
            internal readonly Dictionary<int, long> ConsumedProduction;
        }
    }
}
