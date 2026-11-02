using System;
using System.Collections.Generic;
using System.Reflection;
using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Networking;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>适配魔法存储普通、批量及递归合成，多人候选还须通过服务器成功确认。</summary>
    internal sealed partial class MagicStorageCraftingSystem : ModSystem
    {
        private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        private readonly List<IDisposable> hooks = new();
        private readonly List<IDisposable> recursionHooks = new();
        private static FieldInfo resultsField, withdrawField, simulationField;
        private static Func<bool> isSimulating;
        private static int worldGeneration;
        [ThreadStatic] private static Operation active;

        public override void PostSetupContent()
        {
            if (!ModLoader.TryGetMod("MagicStorage", out Mod upstream)) return;
            upstreamVersion = upstream.Version?.ToString();
            supported = upstream.Version != null && upstream.Version >= new Version(0, 7, 0, 11);
            if (upstream.Version == null || upstream.Version < new Version(0, 7, 0, 11))
            {
                Mod.Logger.Warn("MagicStorage crafting disabled: requires 0.7.0.11 or later with supported entry signatures.");
                return;
            }
            try
            {
                InstallHooks(upstream.Code, hooks);
                installedCapabilities |= MagicStorageConfirmation.Standard;
                Mod.Logger.Info($"MagicStorage {upstream.Version} standard/batch crafting enabled for single-player.");
            }
            catch (Exception error)
            {
                DisposeHooks(hooks);
                ClearBindings();
                Mod.Logger.Warn($"MagicStorage crafting disabled: {error.Message}");
                return;
            }
            try
            {
                InstallRecursionHooks(upstream.Code, recursionHooks);
                installedCapabilities |= MagicStorageConfirmation.Recursive;
                Mod.Logger.Info($"MagicStorage {upstream.Version} recursive full-chain crafting enabled.");
            }
            catch (Exception error)
            {
                DisposeHooks(recursionHooks);
                ClearRecursionBindings();
                Mod.Logger.Warn($"MagicStorage recursive crafting disabled: {error.Message}");
            }
            try
            {
                InstallNetworkHooks(upstream.Code, networkHooks);
                networkInstalled = true;
                Mod.Logger.Info("MagicStorage multiplayer crafting confirmation installed; recording requires a matching server session.");
            }
            catch (Exception error)
            {
                DisposeHooks(networkHooks);
                Mod.Logger.Warn($"MagicStorage multiplayer crafting disabled: {error.Message}");
            }
        }

        public override void OnWorldLoad() => ResetWorld();
        public override void OnWorldUnload() => ResetWorld();

        public override void Unload()
        {
            UnloadNetwork();
            DisposeHooks(recursionHooks);
            DisposeHooks(hooks);
            ClearBindings();
            ResetWorld();
        }

        /// <summary>审计精确入口，先安装采集叶子和材料成功边界，再重建预编译调用方。</summary>
        internal static void InstallHooks(Assembly code, List<IDisposable> installed, bool installRoot = true)
        {
            Type gui = code.GetType("MagicStorage.CraftingGUI", true);
            Type context = gui.GetNestedType("CraftingContext", BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(gui.FullName, "CraftingContext");
            Type heart = code.GetType("MagicStorage.Components.TEStorageHeart", true);
            resultsField = RequireField(context, "results", typeof(List<Item>));
            withdrawField = RequireField(context, "toWithdraw", typeof(List<Item>));
            simulationField = RequireField(context, "simulation", typeof(bool));
            MethodInfo simulation = Require(gui, "get_SimulatingCrafts", typeof(bool));
            isSimulating = (Func<bool>)simulation.CreateDelegate(typeof(Func<bool>));
            MethodInfo settle = Require(gui, "HandleCraftWithdrawAndDeposit", typeof(List<Item>), heart, typeof(List<Item>), typeof(List<Item>));
            MethodInfo standard = Require(gui, "Craft_DoStandardCraft", typeof(void), context);
            MethodInfo craft = Require(gui, "Craft", typeof(void), typeof(int));
            MethodInfo batch = Require(gui, "AttemptLazyBatchCraft", typeof(bool), context);
            MethodInfo repeated = Require(gui, "AttemptCraft", typeof(void), typeof(Func<,>).MakeGenericType(context, typeof(bool)), context);
            MethodInfo created = typeof(RecipeLoader).GetMethod(nameof(RecipeLoader.OnCraft),
                new[] { typeof(Item), typeof(Recipe), typeof(List<Item>), typeof(Item) });
            installed.Add(new Hook(created,
                (Action<Action<Item, Recipe, List<Item>, Item>, Item, Recipe, List<Item>, Item>)HandleCreated));
            installed.Add(new ILHook(settle, InstallAcceptedBoundary));
            installed.Add(new ILHook(batch, RecompileCaller));
            installed.Add(new ILHook(repeated, RecompileCaller));
            installed.Add(new Hook(standard, (Action<Action<object>, object>)HandleStandard));
            // 0.7.0.11 经编译器闭包调用普通步骤；只重建 Craft 本体不能移除闭包内旧的内联副本。
            foreach (Type nested in gui.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (MethodInfo caller in nested.GetMethods(Methods))
                    if (caller.Name.StartsWith("<Craft>b__", StringComparison.Ordinal) && caller.GetMethodBody() != null)
                        installed.Add(new ILHook(caller, RecompileCaller));
            if (installRoot) installed.Add(new Hook(craft, (Action<Action<int>, int>)HandleCraft));
        }

        /// <summary>仅在已审计的提取循环之后、结果枚举之前插入标记；回退路径跳过此处。</summary>
        private static void InstallAcceptedBoundary(ILContext il)
        {
            var matches = new List<Instruction>();
            foreach (Instruction instruction in il.Body.Instructions)
            {
                if (instruction.OpCode.Code == Code.Ldarg_2
                    && instruction.Previous?.Operand is MethodReference clear
                    && clear.Name == "Clear" && clear.DeclaringType.FullName == "System.Collections.Generic.List`1<Terraria.Item>"
                    && instruction.Next?.Operand is MethodReference enumerate && enumerate.Name == "GetEnumerator")
                    matches.Add(instruction);
            }
            if (matches.Count != 1) throw new InvalidOperationException("Unique MagicStorage material-accepted boundary not found.");
            var cursor = new ILCursor(il);
            cursor.Goto(matches[0], MoveType.Before);
            cursor.Emit(OpCodes.Ldarg_1);
            cursor.Emit(OpCodes.Ldarg_2);
            cursor.EmitDelegate<Action<List<Item>, List<Item>>>(MarkAccepted);
        }

        /// <summary>让已预编译的真实产物回调调用方重新生成，避免旧内联副本绕过叶子。</summary>
        private static void RecompileCaller(ILContext il) => new ILCursor(il).Emit(OpCodes.Nop);

        /// <summary>固定本地发起者；多人仅在双方已协商能力时创建待确认候选。</summary>
        private static void HandleCraft(Action<int> orig, int amount)
        {
            bool collecting = Main.netMode == NetmodeID.SinglePlayer || Main.netMode == NetmodeID.MultiplayerClient
                && networkInstalled && client.Capabilities != 0;
            Player player = collecting && Main.myPlayer >= 0 && Main.myPlayer < Main.maxPlayers
                ? Main.player[Main.myPlayer] : null;
            bool eligible = player != null && player.active;
            bool networked = Main.netMode == NetmodeID.MultiplayerClient;
            ExecuteCraft(value => { if (active != null) active.Networked = networked; orig(value); }, amount, eligible,
                (type, units) => player.GetModPlayer<StatisticsPlayer>().RecordItemCraft(type, units));
        }

        /// <summary>普通和递归操作各自隔离；单人成功直接提交，多人等待服务器确认。</summary>
        internal static void ExecuteCraft(Action<int> orig, int amount, bool eligible, Action<int, long> record)
        {
            Operation previous = active;
            var operation = eligible ? new Operation(worldGeneration) : null;
            active = operation;
            bool completed = false;
            try
            {
                orig(amount);
                completed = true;
                if (operation != null && operation.Generation == worldGeneration)
                {
                    Dictionary<int, long> totals = operation.Pending != 0 ? client.Complete(operation.Pending, Main.GameUpdateCount)
                        : operation.Accepted && !operation.Networked ? GetTotals(operation) : null;
                    if (totals != null) foreach (var output in totals) record(output.Key, output.Value);
                }
            }
            finally
            {
                try { if (!completed && operation?.Pending > 0) AbortPending(operation.Pending); }
                finally { active = previous; }
            }
        }

        /// <summary>普通产物聚合或递归链内消耗加实际交付，最终仅返回非零不可变数量候选。</summary>
        private static Dictionary<int, long> GetTotals(Operation operation)
        {
            if (operation.Recursive && operation.Plan == null) return null;
            var totals = operation.Recursive ? new Dictionary<int, long>(operation.Plan.ConsumedProduction) : new Dictionary<int, long>();
            foreach (var output in operation.Outputs.Values) AddQuantity(totals, output.Type, output.Units);
            foreach (int type in new List<int>(totals.Keys)) if (totals[type] <= 0) totals.Remove(type);
            return totals;
        }

        /// <summary>只有实际普通合成执行期间开放采集，递归及列表预览不会进入此作用域。</summary>
        private static void HandleStandard(Action<object> orig, object context)
        {
            Operation operation = active;
            if (operation == null) { orig(context); return; }
            object previous = operation.Context;
            operation.Context = context;
            try { orig(context); }
            finally { operation.Context = previous; }
        }

        /// <summary>在回调结束后采集合成上下文真实持有的产物；重复通知只更新同一对象的快照。</summary>
        private static void HandleCreated(Action<Item, Recipe, List<Item>, Item> orig, Item item, Recipe recipe, List<Item> consumed, Item destination)
        {
            orig(item, recipe, consumed, destination);
            Operation operation = active;
            object context = operation?.Context;
            if (context == null || isSimulating() || (bool)simulationField.GetValue(context)
                || item == null || item.IsAir || !((List<Item>)resultsField.GetValue(context)).Contains(item)) return;
            operation.SettlementContext = context;
            operation.Outputs[item] = (item.type, item.stack);
        }

        /// <summary>用合成上下文的结果对象核对压缩后的提交，区分成功交付与失败材料返还。</summary>
        private static void MarkAccepted(List<Item> withdrawn, List<Item> results)
        {
            MarkServerAccepted(withdrawn, results);
            Operation operation = active;
            if (operation == null || operation.Accepted || !MatchesOperation(operation, withdrawn, results)) return;
            operation.Accepted = true;
        }

        /// <summary>单人结算和客户端请求都须来自本操作实际产生并压缩后的列表。</summary>
        private static bool MatchesOperation(Operation operation, List<Item> withdrawn, List<Item> results)
        {
            if (operation.SettlementContext == null || operation.Outputs.Count == 0
                || !ReferenceEquals(results, resultsField.GetValue(operation.SettlementContext))
                || !ReferenceEquals(withdrawn, withdrawField.GetValue(operation.SettlementContext))
                || operation.Recursive && operation.Plan == null) return false;
            // CompactItemList 会创建新列表并合并堆叠，留下的对象仍来自该普通操作的产物集合。
            foreach (Item item in results)
                if (!operation.Outputs.ContainsKey(item)) return false;
            return results.Count != 0;
        }

        /// <summary>按声明类型和完整签名验证入口，避免上游重载变化被误判为可用。</summary>
        private static MethodInfo Require(Type type, string name, Type returns, params Type[] args)
        {
            MethodInfo method = type.GetMethod(name, Methods, null, args, null);
            if (method == null || !method.IsStatic || method.ReturnType != returns || method.ContainsGenericParameters || method.GetMethodBody() == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        /// <summary>校验普通合成上下文保存的实际结果字段。</summary>
        private static FieldInfo RequireField(Type type, string name, Type expected)
        {
            FieldInfo field = type.GetField(name, Methods);
            if (field == null || field.IsStatic || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        /// <summary>逆序卸载完整或部分安装的可选 Hook。</summary>
        internal static void DisposeHooks(List<IDisposable> installed)
        {
            for (int index = installed.Count - 1; index >= 0; index--) installed[index].Dispose();
            installed.Clear();
        }

        /// <summary>释放上游反射引用。</summary>
        private static void ClearBindings() { resultsField = withdrawField = simulationField = null; isSimulating = null; ClearRecursionBindings(); }

        /// <summary>换世界或断线使尚未提交的操作立即失效。</summary>
        private static void ResetWorld() { unchecked { worldGeneration++; } active = null; ResetRecursion(); ResetConfirmation(); }

        /// <summary>只在同步调用链中存活的合成候选，不持有玩家或世界对象。</summary>
        private sealed class Operation
        {
            /// <summary>固定操作开始时的世界代次。</summary>
            internal Operation(int generation) { Generation = generation; }
            internal readonly int Generation;
            internal object Context, SettlementContext;
            internal bool Accepted;
            internal bool Recursive;
            internal bool Networked;
            internal ulong Pending;
            internal ProductionPlan Plan;
            internal readonly Dictionary<Item, (int Type, int Units)> Outputs = new(ReferenceEqualityComparer.Instance);
        }
    }
}
