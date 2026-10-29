using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.Networking;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// M3 单人工具适配。按能力组挂接真实操作入口，不依赖手持物品猜测世界修改来源。
    /// 多人使用服务器任务或客户端施工验证，未协商能力继续抑制；不改变上游消耗或返回值。
    /// </summary>
    internal sealed class ImproveGameToolSystem : ModSystem
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        private const string Items = "ImproveGame.Content.Items.";
        private const string Vein = "ImproveGame.Content.Functions.VeinMiner.VeinMinerSystem";
        private readonly List<Hook> hooks = new();
        private bool observesWorld;
        [ThreadStatic] private static Work active;
        [ThreadStatic] private static bool inVein;
        private static ConditionalWeakTable<object, Work> selectors = new();
        private static ConditionalWeakTable<object, Work> robots = new();
        private static Type burstType, wallSelectorType, robotType;
        private static FieldInfo robotWalls, minerIndex;
        private static PropertyInfo robotIndex;
        private static MethodInfo burstRectangle;
        private static bool robotEnabled;
        private static bool burstEnabled, wallEnabled;

        /// <summary>保留上游实际枚举类型，避免用 int 代替 byte 枚举而破坏 Hook ABI。</summary>
        private delegate void SpaceOperation<T>(Player player, int x, int y, HashSet<Point> selected,
            T placeType, BlockType blockType, ref bool sound, Dictionary<int, int> consumed) where T : struct;

        public override void PostSetupContent()
        {
            if (!ModLoader.TryGetMod("ImproveGame", out Mod mod)) return;
            if (!ImproveGameVersionPolicy.Supports(mod.Version))
            {
                Mod.Logger.Warn($"ImproveGame {mod.Version}: tool collection requires version 1.8.2 or later.");
                return;
            }
            Assembly code = mod.Code;
            bool space = InstallGroup("SpaceWand", group => InstallSpace(code, group));
            int selectorStart = hooks.Count;
            bool burst = burstEnabled = InstallGroup("MagickWand/StarburstWand", group => InstallBurst(code, group));
            bool wall = wallEnabled = InstallGroup("WallPlaceSelectorMode", group => InstallWallSelector(code, group));
            if ((burst || wall) && !InstallGroup("selector task ownership", group => InstallSelectors(code, group)))
            {
                for (int i = hooks.Count - 1; i >= selectorStart; i--) hooks[i].Dispose();
                hooks.RemoveRange(selectorStart, hooks.Count - selectorStart);
                burst = wall = burstEnabled = wallEnabled = false;
            }
            robotEnabled = InstallGroup("WallRobot", group => InstallRobot(code, group));
            bool vein = InstallGroup("VeinMiner", group => InstallVein(code, group));
            ImproveGameMultiplayerSystem.PublishCapabilities(space, burst, wall, vein);
            ImproveGameMultiplayerSystem.PublishClientTools(burst, robotEnabled);
            ModContent.GetInstance<ImproveGameMultiplayerSystem>().InstallReceivers(mod);
            observesWorld = space || burst || wall || robotEnabled || vein;
            if (observesWorld)
            {
                On_WorldGen.KillTile += HandleKillTile;
                On_WorldGen.KillWall += HandleKillWall;
            }
            Mod.Logger.Info($"ImproveGame M3 installed: space={space}, burst={burst}, wallSelector={wall}, robot={robotEnabled}, vein={vein}; multiplayer requires negotiated M4 execution authority or client-step verification.");
        }

        public override void OnWorldLoad() => active = null;
        public override void OnWorldUnload() => active = null;

        public override void Unload()
        {
            if (observesWorld)
            {
                On_WorldGen.KillTile -= HandleKillTile;
                On_WorldGen.KillWall -= HandleKillWall;
            }
            for (int i = hooks.Count - 1; i >= 0; i--) hooks[i].Dispose();
            hooks.Clear();
            observesWorld = robotEnabled = burstEnabled = wallEnabled = false;
            ImproveGameMultiplayerSystem.PublishCapabilities(false, false, false, false);
            ImproveGameMultiplayerSystem.PublishClientTools(false, false);
            active = null;
            inVein = false;
            selectors = new ConditionalWeakTable<object, Work>();
            robots = new ConditionalWeakTable<object, Work>();
            burstType = wallSelectorType = robotType = null;
            robotWalls = minerIndex = null;
            robotIndex = null;
            burstRectangle = null;
        }

        /// <summary>独立安装并在失败时撤销整组，避免只挂接部分调用链。</summary>
        private bool InstallGroup(string name, Action<List<Hook>> install)
        {
            var group = new List<Hook>();
            try { install(group); hooks.AddRange(group); return true; }
            catch (Exception error)
            {
                for (int i = group.Count - 1; i >= 0; i--) group[i].Dispose();
                Mod.Logger.Warn($"ImproveGame M3 {name} disabled: {error.Message}");
                return false;
            }
        }

        /// <summary>按真实签名查找入口，拒绝仅同名但参数或返回值不匹配的方法。</summary>
        private static MethodInfo Require(Assembly code, string type, string name, Type result, bool isStatic, params Type[] args)
        {
            MethodInfo method = code.GetType(type)?.GetMethod(name, Flags, null, args, null);
            if (method == null || method.ReturnType != result || method.IsStatic != isStatic)
                throw new MissingMethodException(type, name);
            return method;
        }

        /// <summary>空间工具逐格入口直接携带玩家与精确选区，保留 ref 音效参数和真实枚举类型。</summary>
        private static void InstallSpace(Assembly code, List<Hook> group)
        {
            Type place = code.GetType(Items + "PlaceType") ?? throw new TypeLoadException("PlaceType");
            if (!place.IsEnum) throw new TypeLoadException("PlaceType is not an enum");
            MethodInfo method = Require(code, Items + "SpaceWand", "OperateTile", typeof(void), true,
                typeof(Player), typeof(int), typeof(int), typeof(HashSet<Point>), place, typeof(BlockType),
                typeof(bool).MakeByRefType(), typeof(Dictionary<int, int>));
            MethodInfo target = typeof(ImproveGameToolSystem).GetMethod(nameof(HandleSpace), Flags).MakeGenericMethod(place);
            group.Add(new Hook(method, target));
        }

        /// <summary>按成功结果结算物块替换；相同类型仅调整坡形时没有内容差，不计拆放。</summary>
        private static void HandleSpace<T>(SpaceOperation<T> orig, Player player, int x, int y, HashSet<Point> selected,
            T placeType, BlockType blockType, ref bool sound, Dictionary<int, int> consumed) where T : struct
        {
            using (new Step(new Work(player, (i, j) => selected?.Contains(new Point(i, j)) == true, ImproveGameCapability.Space)))
            {
                Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Replace);
                orig(player, x, y, selected, placeType, blockType, ref sound, consumed);
            }
        }

        /// <summary>先挂逐格与固定入口，再由选择器工厂恢复跨帧归属；星爆继承同一实现。</summary>
        private static void InstallBurst(Assembly code, List<Hook> group)
        {
            burstType = code.GetType(Items + "MagickWand") ?? throw new TypeLoadException("MagickWand");
            burstRectangle = Require(code, Items + "MagickWand", "GetRectangle", typeof(Rectangle), false, typeof(Player));
            MethodInfo modify = Require(code, Items + "MagickWand", "ModifySelectedTiles", typeof(bool), false,
                typeof(Player), typeof(int), typeof(int));
            group.Add(new Hook(modify, (Func<Func<object, Player, int, int, bool>, object, Player, int, int, bool>)HandleBurstCell));
            MethodInfo fixedMode = Require(code, Items + "MagickWand", "FixedModeAction", typeof(void), false, typeof(Player));
            group.Add(new Hook(fixedMode, (Action<Action<object, Player>, object, Player>)HandleFixedBurst));
        }

        /// <summary>固定拆除只观察计算出的真实矩形，模式关闭或破坏失败时没有状态差。</summary>
        private static void HandleFixedBurst(Action<object, Player> orig, object item, Player player)
        {
            Rectangle area = Main.netMode != NetmodeID.Server
                ? (Rectangle)burstRectangle.Invoke(item, new object[] { player }) : default;
            using (new Step(new Work(player, area.Contains, ImproveGameCapability.FixedBurst))) orig(item, player);
        }

        /// <summary>框选拆除在完整选区任务中允许目标内联动；独立调用只归属该直接格位。</summary>
        private static bool HandleBurstCell(Func<object, Player, int, int, bool> orig, object item, Player player, int x, int y)
        {
            Work work = active?.Player == player ? active : new Work(player, (i, j) => i == x && j == y, ImproveGameCapability.BurstSelector);
            using (new Step(work))
            {
                Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Remove);
                Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Remove);
                return orig(item, player, x, y);
            }
        }

        /// <summary>科技框选每次调用可能只拆墙或只放墙，分别保存两侧实际结果。</summary>
        private static void InstallWallSelector(Assembly code, List<Hook> group)
        {
            wallSelectorType = code.GetType(Items + "WallPlaceSelectorMode") ?? throw new TypeLoadException("WallPlaceSelectorMode");
            group.Add(new Hook(Require(code, Items + "WallPlaceSelectorMode", "ModifySelectedTiles", typeof(bool), false,
                typeof(Player), typeof(int), typeof(int)),
                (Func<Func<object, Player, int, int, bool>, object, Player, int, int, bool>)HandleWallCell));
        }

        /// <summary>科技框选逐格记录，取消后不虚构尚未发生的放置。</summary>
        private static bool HandleWallCell(Func<object, Player, int, int, bool> orig, object item, Player player, int x, int y)
        {
            Work work = active?.Player == player ? active : new Work(player, (i, j) => i == x && j == y, ImproveGameCapability.WallSelector);
            using (new Step(work))
            {
                Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Replace);
                return orig(item, player, x, y);
            }
        }

        /// <summary>先挂实际推进入口再挂工厂；按具体迭代器保留玩家和选区，避免跨任务串归属。</summary>
        private static void InstallSelectors(Assembly code, List<Hook> group)
        {
            MethodInfo factory = Require(code, Items + "SelectorItem", "ModifyTiles", typeof(IEnumerator), false,
                typeof(Player), typeof(Rectangle));
            Type state = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            MethodInfo next = state?.GetMethod("MoveNext", Flags, null, Type.EmptyTypes, null);
            if (next?.ReturnType != typeof(bool)) throw new MissingMethodException("SelectorItem iterator.MoveNext");
            group.Add(new Hook(next, (Func<Func<object, bool>, object, bool>)HandleSelectorNext));
            group.Add(new Hook(factory, (Func<Func<object, Player, Rectangle, IEnumerator>, object, Player, Rectangle, IEnumerator>)HandleSelectorFactory));
        }

        /// <summary>只绑定明确适配的选择器，不给其他 SelectorItem 子类赋予统计归属。</summary>
        private static IEnumerator HandleSelectorFactory(Func<object, Player, Rectangle, IEnumerator> orig,
            object item, Player player, Rectangle area)
        {
            IEnumerator result = orig(item, player, area);
            if (result != null && ((burstEnabled && burstType?.IsInstanceOfType(item) == true)
                || (wallEnabled && wallSelectorType?.IsInstanceOfType(item) == true)))
            {
                selectors.Remove(result);
                selectors.Add(result, new Work(player, area.Contains, burstType?.IsInstanceOfType(item) == true
                    ? ImproveGameCapability.BurstSelector : ImproveGameCapability.WallSelector));
            }
            return result;
        }

        /// <summary>逐次恢复任务上下文；旧世界任务继续执行时只抑制，不重新归属给当前角色。</summary>
        private static bool HandleSelectorNext(Func<object, bool> orig, object iterator)
        {
            if (!selectors.TryGetValue(iterator, out Work work)) return orig(iterator);
            try
            {
                using (new Step(work))
                {
                    bool more = orig(iterator);
                    if (!more) selectors.Remove(iterator);
                    return more;
                }
            }
            catch { selectors.Remove(iterator); throw; }
        }

        /// <summary>机器人按实体所有者而不是当前手持归属，入口签名及目标列表在安装时检查。</summary>
        private static void InstallRobot(Assembly code, List<Hook> group)
        {
            const string type = "ImproveGame.Content.Projectiles.WallRobot";
            robotType = code.GetType(type) ?? throw new TypeLoadException(type);
            robotWalls = robotType.GetField("Walls", Flags);
            robotIndex = robotType.GetProperty("index", Flags);
            if (robotWalls?.FieldType != typeof(List<Point>) || robotIndex?.PropertyType != typeof(int))
                throw new MissingMemberException(type, "Walls/index");
            group.Add(new Hook(Require(code, type, "AI", typeof(void), false), (Action<Action<object>, object>)HandleRobot));
        }

        /// <summary>在真实生成时冻结角色及世界身份；未观察到生成的实体不会在首次 AI 时猜测归属。</summary>
        internal static void ObserveRobotSpawn(Projectile projectile)
        {
            if (!robotEnabled || projectile?.ModProjectile == null || !robotType.IsInstanceOfType(projectile.ModProjectile)) return;
            robots.Remove(projectile.ModProjectile);
            robots.Add(projectile.ModProjectile, new Work(GetPlayer(projectile.owner), (_, _) => false, ImproveGameCapability.Robot));
        }

        /// <summary>只观察此次 AI 实际将处理的一格墙；原有组件索引与消耗完全由上游决定。</summary>
        private static void HandleRobot(Action<object> orig, object robot)
        {
            if (!robots.TryGetValue(robot, out Work work))
            { using (TileChangeScope.Suppress()) orig(robot); return; }
            Projectile projectile = ((ModProjectile)robot).Projectile;
            var walls = (List<Point>)robotWalls.GetValue(robot);
            int index = (int)robotIndex.GetValue(robot);
            Point point = default;
            bool hasTarget = projectile.owner == work.Player?.whoAmI && walls != null && index >= 0 && index < walls.Count;
            if (hasTarget) point = walls[index];
            using (new Step(work.ForTargets((x, y) => hasTarget && x == point.X && y == point.Y)))
            {
                if (hasTarget) Observe(point.X, point.Y, TileChangeLayer.Wall, TileChangeIntent.Replace);
                orig(robot);
            }
        }

        /// <summary>扩展矿格先挂叶子，再挂带明确 MinerIndex 的连锁入口，首格由原版采掘管线处理。</summary>
        private static void InstallVein(Assembly code, List<Hook> group)
        {
            minerIndex = code.GetType(Vein)?.GetField("MinerIndex", Flags);
            if (minerIndex?.FieldType != typeof(int)) throw new MissingFieldException(Vein, "MinerIndex");
            group.Add(new Hook(Require(code, Vein, "BreakAllOres", typeof(void), true, typeof(List<Point>)),
                (Action<Action<List<Point>>, List<Point>>)HandleOres));
            group.Add(new Hook(Require(code, Vein, "DoVeinMiningAt", typeof(void), true, typeof(Point), typeof(int)),
                (Action<Action<Point, int>, Point, int>)HandleVein));
        }

        /// <summary>只有显式连锁入口中的有效矿工索引才提供归属，不在任意世界逻辑中读取全局索引。</summary>
        private static void HandleVein(Action<Point, int> orig, Point center, int type)
        {
            using (new Step(new Work(GetPlayer((int)minerIndex.GetValue(null)), (_, _) => false, ImproveGameCapability.Vein), vein: true)) orig(center, type);
        }

        /// <summary>先捕获真实处理清单，避免连锁先破坏了后续矿格而漏掉其旧类型。</summary>
        private static void HandleOres(Action<List<Point>> orig, List<Point> points)
        {
            if (active == null || !inVein) { orig(points); return; }
            var set = points == null ? new HashSet<Point>() : new HashSet<Point>(points);
            using (new Step(active.ForTargets((x, y) => set.Contains(new Point(x, y))), vein: true))
            {
                foreach (Point point in set) Observe(point.X, point.Y, TileChangeLayer.Tile, TileChangeIntent.Remove);
                orig(points);
            }
        }

        /// <summary>越界或断开的所有者不提供角色归属。</summary>
        private static Player GetPlayer(int index) => index >= 0 && index < Main.player.Length ? Main.player[index] : null;

        /// <summary>只注册当前工具明确选择的目标，普通远程同步或环境破坏不能创建新目标。</summary>
        private static void Observe(int x, int y, TileChangeLayer layer, TileChangeIntent intent)
        {
            if (active?.CanRecord == true && active.Allows(x, y)) TileChangeScope.Observe(x, y, layer, intent, direct: true);
        }

        /// <summary>补获选区内提前联动的实际拆除，同时保留同类型重放的拆除证据。</summary>
        private static void HandleKillTile(On_WorldGen.orig_KillTile orig, int x, int y, bool fail, bool effectOnly, bool noItem)
        {
            Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Remove);
            orig(x, y, fail, effectOnly, noItem);
            TileChangeScope.NoteRemoval(x, y, TileChangeLayer.Tile);
        }

        /// <summary>记录实际墙拆除，不因后续成功放置而抑制旧墙数量。</summary>
        private static void HandleKillWall(On_WorldGen.orig_KillWall orig, int x, int y, bool fail)
        {
            Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Remove);
            orig(x, y, fail);
            TileChangeScope.NoteRemoval(x, y, TileChangeLayer.Wall);
        }

        /// <summary>保存不可变所有者、世界会话及目标判定；不读取当前手持物品。</summary>
        private sealed class Work
        {
            /// <summary>绑定实际入口中的玩家。</summary>
            internal Work(Player player, Func<int, int, bool> allows, ImproveGameCapability capability = ImproveGameCapability.None)
                : this(player, allows, TileChangeScope.WorldSession, capability, ImproveGameMultiplayerSystem.Capture(player, capability)) { }
            /// <summary>创建同一会话中的目标子步骤。</summary>
            private Work(Player player, Func<int, int, bool> allows, long session, ImproveGameCapability capability,
                ImproveGameMultiplayerSystem.Authority authority)
            { Player = player; Allows = allows; Session = session; Capability = capability; Authority = authority; }
            internal Player Player { get; }
            internal Func<int, int, bool> Allows { get; }
            private long Session { get; }
            internal ImproveGameCapability Capability { get; }
            internal ImproveGameMultiplayerSystem.Authority Authority { get; }
            internal bool CanRecord => Session == TileChangeScope.WorldSession &&
                (Authority?.Allows(Player, Capability) == true || (ImproveGameMultiplayerSystem.CanUseClient(Player, Capability))
                || (Main.netMode == NetmodeID.SinglePlayer && !Main.gameMenu && !Main.dedServ
                && Player?.active == true && Player.whoAmI == Main.myPlayer && GetPlayer(Main.myPlayer) == Player));
            /// <summary>继承原会话，不能在新世界重建旧实体/任务的身份。</summary>
            internal Work ForTargets(Func<int, int, bool> allows) => new Work(Player, allows, Session, Capability, Authority);
        }

        /// <summary>同步步骤恢复/释放上下文，嵌套同一玩家时复用共用结算以避免首格与扩展重复。</summary>
        private sealed class Step : IDisposable
        {
            private readonly Work previous;
            private readonly bool previousVein;
            private readonly TileChangeScope scope;
            private readonly ImproveGameMultiplayerSystem.ClientStep candidate;
            /// <summary>在执行原始逻辑前建立归属；多人或旧会话在全部嵌套入口中抑制计数。</summary>
            internal Step(Work work, bool vein = false)
            {
                previous = active;
                previousVein = inVein;
                active = work;
                inVein = vein;
                candidate = work.CanRecord ? ImproveGameMultiplayerSystem.BeginClientStep(work.Player, work.Capability) : null;
                scope = !work.CanRecord ? TileChangeScope.Suppress() : work.Authority != null
                    ? work.Authority.Begin(work.Player) : TileChangeScope.Begin(work.Player, directTargetsOnly: true);
            }
            /// <summary>异常时同样恢复上一层，不留下跨帧静态玩家状态。</summary>
            public void Dispose()
            {
                try { scope.Dispose(); }
                finally { try { candidate?.Dispose(); } finally { active = previous; inVein = previousVein; } }
            }
        }
    }
}
