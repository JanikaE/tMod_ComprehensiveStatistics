using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>采集原版桶/海绵和液体魔杖的即时体积差；多人只在权威确认后记账。</summary>
    internal sealed class StatisticsLiquidSystem : ModSystem
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        private const string WandName = "ImproveGame.Content.Items.LiquidWand";
        private const string SelectorName = "ImproveGame.Content.Items.SelectorItem";
        private readonly List<Hook> wandHooks = new();
        private static Type wandType;
        private static ConditionalWeakTable<object, Work> tasks = new();
        [ThreadStatic] private static Work activeTask;
        [ThreadStatic] private static bool directWaterCall;
        internal static bool WandInstalled { get; private set; }
        internal static bool NativeBoundaryInstalled { get; private set; }

        public override void Load()
        {
            try { IL_MessageBuffer.GetData += InstallNativeWriteBoundary; }
            catch (Exception error)
            {
                NativeBoundaryInstalled = false;
                Mod.Logger.Warn($"Native multiplayer liquid write-boundary hook unavailable: {error.Message}");
            }
            if (!NativeBoundaryInstalled) Mod.Logger.Warn("Native multiplayer liquid collection disabled: accepted-write IL boundary not found.");
            // 世界更新边界先于操作入口安装，避免把帧修复引发的环境变化算成工具直接变化。
            On_WorldGen.SquareTileFrame += HandleFrame;
            On_Liquid.Update += HandleLiquidUpdate;
            On_Liquid.AddWater += HandleAddWater;
            On_NetMessage.SendData += HandleSendData;
            On_NetMessage.sendWater += HandleSendWater;
            On_Player.ItemCheck_UseBuckets += HandleBuckets;
            ImproveGameMultiplayerSystem.PublishLiquid(NativeBoundaryInstalled, WandInstalled);
        }

        public override void OnWorldLoad() => ResetWorld();
        public override void OnWorldUnload() => ResetWorld();

        public override void Unload()
        {
            On_Player.ItemCheck_UseBuckets -= HandleBuckets;
            On_NetMessage.sendWater -= HandleSendWater;
            On_NetMessage.SendData -= HandleSendData;
            On_Liquid.Update -= HandleLiquidUpdate;
            On_Liquid.AddWater -= HandleAddWater;
            On_WorldGen.SquareTileFrame -= HandleFrame;
            IL_MessageBuffer.GetData -= InstallNativeWriteBoundary;
            NativeBoundaryInstalled = false;
            for (int i = wandHooks.Count - 1; i >= 0; i--) wandHooks[i].Dispose();
            wandHooks.Clear();
            WandInstalled = false;
            ImproveGameMultiplayerSystem.PublishLiquid(false, false);
            wandType = null;
            ResetWorld();
            tasks = new ConditionalWeakTable<object, Work>();
        }

        /// <summary>由工具安装协调入口优先调用，确保液体叶子与工厂先于共享 Selector 接收入口的 JIT。</summary>
        internal void InstallImproveGame(Mod upstream)
        {
            if (wandHooks.Count != 0) { ImproveGameMultiplayerSystem.PublishLiquid(NativeBoundaryInstalled, WandInstalled); return; }
            if (!ImproveGameVersionPolicy.Supports(upstream.Version)) return;
            var group = new List<Hook>();
            try
            {
                InstallWandHooks(upstream.Code, group);
                wandHooks.AddRange(group);
                WandInstalled = true;
                ImproveGameMultiplayerSystem.PublishLiquid(NativeBoundaryInstalled, true);
                Mod.Logger.Info($"ImproveGame {upstream.Version}: liquid collection installed; multiplayer requires protocol 3 confirmation.");
            }
            catch (Exception error)
            {
                for (int i = group.Count - 1; i >= 0; i--) group[i].Dispose();
                wandType = null;
                WandInstalled = false;
                ImproveGameMultiplayerSystem.PublishLiquid(NativeBoundaryInstalled, false);
                Mod.Logger.Warn($"ImproveGame LQ3 liquid collection disabled: {error.Message}");
            }
        }

        /// <summary>
        /// 消息 48 写完真实液体类型后立即取证，不能依赖可能已被内联的帧方法。
        /// 其他消息的同名写入仅调用无活动入站时的空出口；找不到真实写入模式则不发布原版多人能力。
        /// </summary>
        private static void InstallNativeWriteBoundary(ILContext il)
        {
            var cursor = new ILCursor(il);
            int matches = 0;
            while (cursor.TryGotoNext(MoveType.After, instruction => instruction.Operand is MethodReference method
                && method.DeclaringType.FullName == "Terraria.Tile" && method.Name == "liquidType"
                && method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Void"))
            {
                cursor.EmitDelegate<Action>(ImproveGameMultiplayerSystem.CaptureNativeAfterWrite);
                matches++;
            }
            NativeBoundaryInstalled = matches > 0;
            ImproveGameMultiplayerSystem.PublishLiquid(NativeBoundaryInstalled, WandInstalled);
        }

        /// <summary>按实际声明、返回值及参数签名验证可选入口，避免只凭名称挂接。</summary>
        private static MethodInfo Require(Type type, string name, Type returns, params Type[] parameters)
        {
            MethodInfo method = type?.GetMethod(name, Flags, null, parameters, null);
            if (method == null || method.IsStatic || method.ReturnType != returns)
                throw new MissingMethodException(type?.FullName, name);
            return method;
        }

        /// <summary>先装叶子、MoveNext 与工厂，最后重建预编译的框选调用方；高级魔杖共用叶子。</summary>
        private static void InstallWandHooks(Assembly code, List<Hook> group)
        {
            wandType = code.GetType(WandName) ?? throw new TypeLoadException(WandName);
            MethodInfo cell = Require(wandType, "ModifySelectedTiles", typeof(bool), typeof(Player), typeof(int), typeof(int));
            Type advanced = code.GetType("ImproveGame.Content.Items.LiquidWandAdvanced");
            if (advanced == null || !wandType.IsAssignableFrom(advanced)
                || advanced.GetMethod("ModifySelectedTiles", Flags, null, new[] { typeof(Player), typeof(int), typeof(int) }, null) != null)
                throw new MissingMemberException("LiquidWandAdvanced no longer inherits the audited liquid operation.");
            MethodInfo factory = Require(code.GetType(SelectorName), "ModifyTiles", typeof(IEnumerator), typeof(Player), typeof(Rectangle));
            Type state = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            MethodInfo next = Require(state, "MoveNext", typeof(bool));
            MethodInfo selection = Require(code.GetType(SelectorName), "DoSelector", typeof(void), typeof(Player));
            group.Add(new Hook(cell, (Func<Func<object, Player, int, int, bool>, object, Player, int, int, bool>)HandleWandCell));
            group.Add(new Hook(next, (Func<Func<object, bool>, object, bool>)HandleNext));
            group.Add(new Hook(factory, (Func<Func<object, Player, Rectangle, IEnumerator>, object, Player, Rectangle, IEnumerator>)HandleFactory));
            // tML 会在 PostSetupContent 前预编译 ImproveGame，短小的工厂可能已被内联进 DoSelector。
            // 只挂工厂不能撤销旧内联副本；最后 Hook 非小型的真实框选入口，让原调用体重新编译，
            // 此时已经安装的工厂 Hook 才能观察任务创建。不能以首次 MoveNext 猜测旧任务的新归属。
            group.Add(new Hook(selection, (Action<Action<object, Player>, object, Player>)HandleSelection));
        }

        /// <summary>保持真实框选行为原样转发；该边界强制重建预编译调用方，使任务工厂不再被旧内联绕过。</summary>
        private static void HandleSelection(Action<object, Player> orig, object item, Player player)
        {
            orig(item, player);
        }

        /// <summary>只绑定已适配液体魔杖的真实任务参数；不使用当前手持或任意 LocalPlayer 推测发起者。</summary>
        private static IEnumerator HandleFactory(Func<object, Player, Rectangle, IEnumerator> orig,
            object item, Player player, Rectangle area)
        {
            IEnumerator iterator = orig(item, player, area);
            if (iterator != null && wandType?.IsInstanceOfType(item) == true)
            {
                tasks.Remove(iterator);
                tasks.Add(iterator, new Work(player, item, area));
            }
            return iterator;
        }

        /// <summary>每次推进恢复同一任务身份，完成或异常时撤销弱关联；旧任务失效后不能进入新世界。</summary>
        private static bool HandleNext(Func<object, bool> orig, object iterator)
        {
            if (!tasks.TryGetValue(iterator, out Work task)) return orig(iterator);
            Work previous = activeTask;
            activeTask = task;
            try
            {
                bool more = orig(iterator);
                if (!more) tasks.Remove(iterator);
                return more;
            }
            catch { tasks.Remove(iterator); throw; }
            finally { activeTask = previous; }
        }

        /// <summary>按任务的直接格位立即结算，返回值仅转发；填充失败和空格吸收不产生差量。</summary>
        private static bool HandleWandCell(Func<object, Player, int, int, bool> orig,
            object item, Player player, int x, int y)
        {
            Work task = activeTask;
            bool valid = task?.Allows(item, player, x, y) == true;
            using (valid ? task.Begin(x, y) : LiquidChangeScope.Begin(null, x, y)) return orig(item, player, x, y);
        }

        /// <summary>原版取液可能消耗邻格，只在对应工具的 3×3 同步执行步骤内比较真实状态。</summary>
        private static void HandleBuckets(On_Player.orig_ItemCheck_UseBuckets orig, Player player, Item item)
        {
            Player owner = IsBucketOrSponge(item) ? player : null;
            using var native = owner == null ? null : ImproveGameMultiplayerSystem.BeginNativeStep(owner,
                Player.tileTargetX, Player.tileTargetY, item.type);
            using (LiquidChangeScope.Begin(owner, Player.tileTargetX, Player.tileTargetY, includeNeighbors: true))
                orig(player, item);
        }

        /// <summary>原版已审计的桶与海绵物品集合；普通物品的空调用不应在热路径创建九格快照。</summary>
        private static bool IsBucketOrSponge(Item item)
        {
            return item != null && item.type is ItemID.EmptyBucket or ItemID.WaterBucket or ItemID.LavaBucket
                or ItemID.HoneyBucket or ItemID.BottomlessBucket or ItemID.SuperAbsorbantSponge
                or ItemID.BottomlessLavaBucket or ItemID.LavaAbsorbantSponge or ItemID.BottomlessHoneyBucket
                or ItemID.HoneyAbsorbantSponge or ItemID.UltraAbsorbantSponge or ItemID.BottomlessShimmerBucket;
        }

        /// <summary>直接修改已发生后先结算，再屏蔽帧修复中的液体反应或其他世界连带变化。</summary>
        private static void HandleFrame(On_WorldGen.orig_SquareTileFrame orig, int x, int y, bool resetFrame)
        {
            ImproveGameMultiplayerSystem.CaptureNativeBeforeFrame(x, y);
            if (!LiquidChangeScope.HasScope && activeTask == null && !ImproveGameMultiplayerSystem.HasNativeStep) { orig(x, y, resetFrame); return; }
            using (LiquidChangeScope.SuppressWorldUpdates()) orig(x, y, resetFrame);
        }

        /// <summary>自然液体演算无论是否被第三方同步触发，都不得继承正在执行的工具归属。</summary>
        private static void HandleLiquidUpdate(On_Liquid.orig_Update orig, Liquid liquid)
        {
            if (!LiquidChangeScope.HasScope && activeTask == null && !ImproveGameMultiplayerSystem.HasNativeStep) { orig(liquid); return; }
            using (LiquidChangeScope.SuppressWorldUpdates()) orig(liquid);
        }

        /// <summary>液体入队可能触发额外世界逻辑，只保留调用之前已经发生的直接修改。</summary>
        private static void HandleAddWater(On_Liquid.orig_AddWater orig, int x, int y)
        {
            if (!LiquidChangeScope.HasScope && activeTask == null && !ImproveGameMultiplayerSystem.HasNativeStep) { orig(x, y); return; }
            using (LiquidChangeScope.SuppressWorldUpdates()) orig(x, y);
        }

        /// <summary>在原版液体同步前建立操作标记，不改写原版包或以客户端预测直接记账。</summary>
        private static void HandleSendWater(On_NetMessage.orig_sendWater orig, int x, int y)
        {
            bool previous = directWaterCall;
            directWaterCall = ImproveGameMultiplayerSystem.HasNativeStep && !LiquidChangeScope.IsSuppressed;
            try { orig(x, y); }
            finally { directWaterCall = previous; }
        }

        /// <summary>所有实际原版液体包占据序号；仅从明确工具 sendWater 发出的包可列入直接操作证据。</summary>
        private static void HandleSendData(On_NetMessage.orig_SendData orig, int message, int to, int ignore,
            NetworkText text, int number, float number2, float number3, float number4, int number5, int number6, int number7)
        {
            using var sending = message == 48 && Main.netMode == NetmodeID.MultiplayerClient
                ? ImproveGameMultiplayerSystem.BeginNativeSend(number, (int)number2, directWaterCall) : null;
            orig(message, to, ignore, text, number, number2, number3, number4, number5, number6, number7);
        }

        /// <summary>撤销会话身份并清理运行态上下文；弱表保留旧身份，旧迭代器不能重新捕获当前角色。</summary>
        private static void ResetWorld()
        {
            LiquidChangeScope.ResetWorld();
            activeTask = null;
            // 不删除旧迭代器的身份，以便继续执行时仍能识别其旧会话并抑制。
        }

        /// <summary>冻结发起玩家、工具实例、选区和世界会话，不跨帧依赖当前手持。</summary>
        private sealed class Work
        {
            private readonly Player player;
            private readonly object item;
            private readonly Rectangle area;
            private readonly long session;
            private readonly bool localAtCreation;
            private readonly ImproveGameMultiplayerSystem.Authority authority;

            /// <summary>在任务工厂处捕获不可更换的身份与会话。</summary>
            internal Work(Player player, object item, Rectangle area)
            {
                this.player = player;
                this.item = item;
                this.area = area;
                session = LiquidChangeScope.WorldSession;
                localAtCreation = LiquidChangeScope.IsLocalSinglePlayer(player);
                authority = ImproveGameMultiplayerSystem.Capture(player, ImproveGameCapability.Liquid);
            }

            /// <summary>只允许同一会话的原任务和选区；多人另须具有仍然有效的服务器权威。</summary>
            internal bool Allows(object source, Player actor, int x, int y)
            {
                return session == LiquidChangeScope.WorldSession && ReferenceEquals(actor, player)
                    && ReferenceEquals(source, item) && area.Contains(x, y)
                    && (localAtCreation && LiquidChangeScope.IsLocalSinglePlayer(actor)
                        || authority?.Allows(actor, ImproveGameCapability.Liquid) == true);
            }

            /// <summary>同一任务选择本地双范围出口或服务器定向队列，两者不会同时写入。</summary>
            internal LiquidChangeScope Begin(int x, int y) => authority != null
                ? authority.BeginLiquid(player, x, y) : LiquidChangeScope.Begin(player, x, y);
        }
    }
}
