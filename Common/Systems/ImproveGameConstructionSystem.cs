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
using Terraria.ModLoader.IO;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 适配 ImproveGame 构造与建造工具；单人直接结算，多人只生成有序入站验证的精确候选。
    /// 不引用上游类型，所有内部入口在安装前按签名及最低版本检查。
    /// </summary>
    internal sealed class ImproveGameConstructionSystem : ModSystem
    {
        private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        private const string Generator = "ImproveGame.Content.Functions.Construction.GenerateCore";
        private const string Helpers = "ImproveGame.Helpers.MyUtils";
        private readonly List<Hook> hooks = new();
        private static ConditionalWeakTable<object, Work> iterators = new();
        private static FieldInfo singleTilePosition;
        private static FieldInfo cleanupWidth;
        private static FieldInfo cleanupHeight;
        private static PropertyInfo buildingWidth, buildingHeight, buildingTiles, buildingHasWall;
        [ThreadStatic] private static Work activeWork;
        [ThreadStatic] private static Player launchPlayer;
        [ThreadStatic] private static int mutationDepth;

        /// <summary>保持带 out 参数的原始家具放置签名，确保调用方仍能收到原有 TileObject。</summary>
        private delegate bool PlaceObject(int x, int y, int type, int style, int direction, out TileObject data);
        /// <summary>定义直接家具放置的 Hook 签名。</summary>
        private delegate bool PlaceObjectHook(PlaceObject orig, int x, int y, int type, int style, int direction, out TileObject data);

        public override void PostSetupContent()
        {
            if (!ModLoader.TryGetMod("ImproveGame", out Mod upstream))
                return;
            if (!ImproveGameVersionPolicy.Supports(upstream.Version))
            {
                Mod.Logger.Warn($"ImproveGame {upstream.Version}: construction collection requires version 1.8.2 or later.");
                return;
            }
            Assembly code = upstream.Code;
            if (!InstallGroup("shared construction helpers", group => InstallHelpers(code, group)))
                return;
            bool construct = InstallGroup("ConstructWand", group => InstallConstruct(code, group));
            bool building = InstallGroup("CreateWand", group => InstallBuilding(code, group));
            ImproveGameMultiplayerSystem.PublishConstruction(construct, building);
            if (construct || building)
                On_WorldGen.KillTile += HandleKillTile;
            hasTileHook = construct || building;
            if (construct)
            {
                On_WorldGen.KillWall += HandleKillWall;
                On_WorldGen.PlaceWire += HandleRedWire;
                On_WorldGen.PlaceWire2 += HandleBlueWire;
                On_WorldGen.PlaceWire3 += HandleGreenWire;
                On_WorldGen.PlaceWire4 += HandleYellowWire;
                On_WorldGen.PlaceActuator += HandleActuator;
            }
            hasWorldHooks = construct;
            Mod.Logger.Info($"ImproveGame M2 installed: ConstructWand={construct}, CreateWand={building}; multiplayer requires negotiated client-step verification.");
        }

        private bool hasWorldHooks;
        private bool hasTileHook;

        public override void OnWorldLoad() => ClearTasks();
        public override void OnWorldUnload() => ClearTasks();

        public override void Unload()
        {
            if (hasTileHook)
                On_WorldGen.KillTile -= HandleKillTile;
            if (hasWorldHooks)
            {
                On_WorldGen.KillWall -= HandleKillWall;
                On_WorldGen.PlaceWire -= HandleRedWire;
                On_WorldGen.PlaceWire2 -= HandleBlueWire;
                On_WorldGen.PlaceWire3 -= HandleGreenWire;
                On_WorldGen.PlaceWire4 -= HandleYellowWire;
                On_WorldGen.PlaceActuator -= HandleActuator;
            }
            for (int index = hooks.Count - 1; index >= 0; index--)
                hooks[index].Dispose();
            hooks.Clear();
            hasWorldHooks = false;
            hasTileHook = false;
            ImproveGameMultiplayerSystem.PublishConstruction(false, false);
            ClearTasks();
            iterators = new ConditionalWeakTable<object, Work>();
            singleTilePosition = null;
            cleanupWidth = cleanupHeight = null;
            buildingWidth = buildingHeight = buildingTiles = buildingHasWall = null;
        }

        /// <summary>逐能力安装并回滚不完整 Hook，避免某组签名变化拖垮模组或污染其他组。</summary>
        private bool InstallGroup(string name, Action<List<Hook>> install)
        {
            var group = new List<Hook>();
            try
            {
                install(group);
                hooks.AddRange(group);
                return true;
            }
            catch (Exception exception)
            {
                for (int index = group.Count - 1; index >= 0; index--)
                    group[index].Dispose();
                Mod.Logger.Warn($"ImproveGame {name} disabled: {exception.Message}");
                return false;
            }
        }

        /// <summary>查找完整签名；返回值、静态性和迭代器实际执行点均不能凭名称猜测。</summary>
        private static MethodInfo Require(Assembly code, string typeName, string name, Type returns,
            bool isStatic, params Type[] args)
        {
            MethodInfo method = code.GetType(typeName)?.GetMethod(name, Methods, null, args, null);
            if (method == null || method.ReturnType != returns || method.IsStatic != isStatic)
                throw new MissingMethodException(typeName, name);
            return method;
        }

        /// <summary>为构造任务绑定发起者，并为每个已审计迭代器工厂和 MoveNext 安装入口。</summary>
        private static void InstallConstruct(Assembly code, List<Hook> group)
        {
            // 必须先挂子阶段/辅助方法，最后挂根迭代器及物品入口。Hook 安装会触发原函数编译，
            // 若先编译调用方，尚未挂钩的小工厂可能被内联；之后再挂工厂也无法拦截已内联副本。
            Type generator = code.GetType(Generator) ?? throw new TypeLoadException(Generator);
            singleTilePosition = null;
            foreach (Type closure in generator.GetNestedTypes(BindingFlags.NonPublic))
            {
                FieldInfo position = closure.GetField("placePosition", Methods);
                if (position?.FieldType != typeof(Point))
                    continue;
                foreach (MethodInfo candidate in closure.GetMethods(Methods))
                    if (candidate.Name.Contains("<GenerateSingleTiles>", StringComparison.Ordinal))
                        singleTilePosition = position;
            }
            if (singleTilePosition == null)
                throw new MissingFieldException(Generator, "GenerateSingleTiles closure.placePosition");

            Type structure = code.GetType("ImproveGame.Content.Functions.Construction.QoLStructure")
                ?? throw new TypeLoadException("QoLStructure");
            cleanupWidth = structure.GetField("Width", Methods);
            cleanupHeight = structure.GetField("Height", Methods);
            if (cleanupWidth?.FieldType != typeof(short) || cleanupHeight?.FieldType != typeof(short))
                throw new MissingFieldException(structure.FullName, "Width/Height");
            foreach (string phase in new[] { "KillTiles", "GenerateWalls", "GenerateSingleTiles", "GenerateMultiTiles", "GenerateOutSet", "SquareTiles", "TextSigns" })
            {
                MethodInfo factory = Require(code, Generator, phase, typeof(IEnumerator), true, structure, typeof(Point));
                group.Add(new Hook(factory, (Func<Func<object, Point, IEnumerator>, object, Point, IEnumerator>)
                    ((orig, data, point) => Bind(orig(data, point), activeWork?.ForPhase(phase,
                        phase == "KillTiles" ? new CleanupArea(point,
                            (short)cleanupWidth.GetValue(data), (short)cleanupHeight.GetValue(data)) : null)))));
                InstallMoveNext(factory, group);
            }
            MethodInfo wall = Require(code, Generator, "TryPlaceWall", typeof(bool), true, typeof(Item), typeof(int), typeof(int));
            group.Add(new Hook(wall, (Func<Func<Item, int, int, bool>, Item, int, int, bool>)
                ((orig, item, x, y) => Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Place, () => orig(item, x, y)))));
            MethodInfo generate = Require(code, Generator, "Generate", typeof(IEnumerator), true, typeof(TagCompound), typeof(Point));
            group.Add(new Hook(generate, (Func<Func<TagCompound, Point, IEnumerator>, TagCompound, Point, IEnumerator>)HandleGenerate));
            InstallMoveNext(generate, group);
            MethodInfo start = Require(code, "ImproveGame.Content.Items.ConstructWand", "StartUseItem", typeof(bool), false, typeof(Player));
            group.Add(new Hook(start, (Func<Func<object, Player, bool>, object, Player, bool>)HandleStart));

        }

        /// <summary>为真实迭代器执行安装 Hook；弱表关联避免取消任务后仍被静态集合持有。</summary>
        private static void InstallMoveNext(MethodInfo factory, List<Hook> group)
        {
            Type state = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            MethodInfo next = state?.GetMethod("MoveNext", Methods, null, Type.EmptyTypes, null);
            if (next?.ReturnType != typeof(bool))
                throw new MissingMethodException(factory.Name, "MoveNext");
            group.Add(new Hook(next, (Func<Func<object, bool>, object, bool>)HandleMoveNext));
        }

        /// <summary>只在物品的真实发起入口捕获玩家，不对任意 Generate 调用使用 Main.LocalPlayer 推测归属。</summary>
        private static bool HandleStart(Func<object, Player, bool> orig, object item, Player player)
        {
            Player previous = launchPlayer;
            launchPlayer = player;
            try { return orig(item, player); }
            finally { launchPlayer = previous; }
        }

        /// <summary>把任务绑定到根迭代器；玩家换手持不会改变该任务的所有者。</summary>
        private static IEnumerator HandleGenerate(Func<TagCompound, Point, IEnumerator> orig, TagCompound tag, Point point)
        {
            return Bind(orig(tag, point), launchPlayer != null ? new Work(launchPlayer, "Generate") : null);
        }

        /// <summary>关联一个具体迭代器，不依赖 Runner 当前只运行一个任务的偶然状态。</summary>
        private static IEnumerator Bind(IEnumerator iterator, Work work)
        {
            if (iterator != null && work != null)
            {
                iterators.Remove(iterator);
                iterators.Add(iterator, work);
            }
            return iterator;
        }

        /// <summary>每次推进时恢复身份，返回或抛异常时清理；阶段间独立结算，保留清理后同类型重建。</summary>
        private static bool HandleMoveNext(Func<object, bool> orig, object iterator)
        {
            if (!iterators.TryGetValue(iterator, out Work work))
                return orig(iterator);
            Work previous = activeWork;
            activeWork = work;
            try
            {
                using var candidate = work.CanRecord && work.IsMutationPhase
                    ? ImproveGameMultiplayerSystem.BeginClientStep(work.Player, ImproveGameCapability.Construct) : null;
                using (work.CanRecord && work.IsMutationPhase
                    ? TileChangeScope.BeginStep(work.Player, directTargetsOnly: true) : TileChangeScope.Suppress())
                {
                    bool result = orig(iterator);
                    if (!result)
                        iterators.Remove(iterator);
                    return result;
                }
            }
            catch
            {
                iterators.Remove(iterator);
                throw;
            }
            finally { activeWork = previous; }
        }

        /// <summary>绑定建造魔杖的真实批次；每次单格或单对象放置仍独立结算，避免整栋净差。</summary>
        private static void InstallBuilding(Assembly code, List<Hook> group)
        {
            const string type = "ImproveGame.Content.Items.CreateWand";
            Type data = code.GetType(type + "+BuildingData") ?? throw new TypeLoadException("BuildingData");
            buildingWidth = data.GetProperty("Width", Methods);
            buildingHeight = data.GetProperty("Height", Methods);
            buildingTiles = data.GetProperty("TileInfos", Methods);
            buildingHasWall = code.GetType(type + "+TileInfo")?.GetProperty("HasWall", Methods);
            if (buildingWidth?.PropertyType != typeof(int) || buildingHeight?.PropertyType != typeof(int)
                || buildingTiles == null || buildingHasWall?.PropertyType != typeof(bool))
                throw new MissingMemberException(type, "BuildingData dimensions/TileInfos/HasWall");
            // 先安装逐格入口，再安装整栋施工入口，避免 TryPlace/TryPlaceWall 被内联进原始施工函数。

            foreach (string name in new[] { "TryPlace", "TryPlaceWall" })
            {
                MethodInfo place = Require(code, type, name, typeof(bool), true, typeof(Item), typeof(Player), typeof(int), typeof(int));
                bool wall = name == "TryPlaceWall";
                group.Add(new Hook(place, (Func<Func<Item, Player, int, int, bool>, Item, Player, int, int, bool>)
                    ((orig, item, player, x, y) => BuildingStep(orig, item, player, x, y, wall))));
            }
            MethodInfo method = Require(code, type, "TryPlaceBuilding", typeof(bool), false, typeof(Player), data);
            group.Add(new Hook(method, (Func<Func<object, Player, object, bool>, object, Player, object, bool>)HandleBuilding));
        }

        /// <summary>仅在调用期间恢复建造所有者；多人原版嵌套入口在此处一并屏蔽。</summary>
        private static bool HandleBuilding(Func<object, Player, object, bool> orig, object item, Player player, object data)
        {
            Work previous = activeWork;
            activeWork = new Work(player, "Building", TileChangeScope.WorldSession,
                Main.netMode != NetmodeID.Server ? GetBuildingCleanup(data) : null);
            try
            {
                if (!activeWork.CanRecord)
                    using (TileChangeScope.Suppress()) return orig(item, player, data);
                using var candidate = ImproveGameMultiplayerSystem.BeginClientStep(player, ImproveGameCapability.Building);
                return orig(item, player, data);
            }
            finally { activeWork = previous; }
        }

        /// <summary>仅登记建筑模板中确实会为铺墙执行清理的格位，不把整个建筑包围框当作拆除目标。</summary>
        private static CleanupArea? GetBuildingCleanup(object data)
        {
            int width = (int)buildingWidth.GetValue(data), height = (int)buildingHeight.GetValue(data);
            long count = (long)width * height;
            if (width <= 0 || height <= 0 || count > 1_000_000
                || buildingTiles.GetValue(data) is not IEnumerable tiles)
                return null;
            var mask = new bool[(int)count];
            int index = 0;
            foreach (object tile in tiles)
            {
                if (index >= mask.Length) return null;
                mask[index++] = (bool)buildingHasWall.GetValue(tile);
            }
            if (index != mask.Length) return null;
            Point center = Main.MouseWorld.ToTileCoordinates();
            return new CleanupArea(new Point(center.X - width / 2, center.Y - height / 2), width - 1, height - 1, mask);
        }

        /// <summary>铺墙时清除的实体物块是独立拆除，墙本身按替换语义结算。</summary>
        private static bool BuildingStep(Func<Item, Player, int, int, bool> orig, Item item, Player player, int x, int y, bool wall)
        {
            if (activeWork?.CanRecord != true || activeWork.Player != player)
                return orig(item, player, x, y);
            using (TileChangeScope.BeginStep(player, directTargetsOnly: true))
            {
                TileChangeScope.Observe(x, y, TileChangeLayer.Tile, wall ? TileChangeIntent.Remove : TileChangeIntent.Replace, direct: true);
                if (wall)
                    TileChangeScope.Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Replace, direct: true);
                return orig(item, player, x, y);
            }
        }

        /// <summary>安装已有辅助方法的观察点，包含直接写格位的箱子和梳妆台路径。</summary>
        private static void InstallHelpers(Assembly code, List<Hook> group)
        {
            group.Add(new Hook(Require(code, Helpers, "PickItemFromArray", typeof(Item), true,
                typeof(Player), typeof(Item[]), typeof(Func<Item, bool>), typeof(bool)),
                (Func<Func<Player, Item[], Func<Item, bool>, bool, Item>, Player, Item[], Func<Item, bool>, bool, Item>)HandleMaterialSearch));
            group.Add(new Hook(Require(code, Helpers, "TryKillTile", typeof(bool), true, typeof(int), typeof(int), typeof(Player)),
                (Func<Func<int, int, Player, bool>, int, int, Player, bool>)((orig, x, y, player) =>
                    Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Remove, () => orig(x, y, player)))));
            group.Add(new Hook(Require(code, Helpers, "TryPlaceTile", typeof(bool), true,
                typeof(int), typeof(int), typeof(Item), typeof(Player), typeof(bool), typeof(bool)),
                (Func<Func<int, int, Item, Player, bool, bool, bool>, int, int, Item, Player, bool, bool, bool>)
                ((orig, x, y, item, player, mute, forced) => Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Place,
                    () => orig(x, y, item, player, mute, forced)))));
            group.Add(new Hook(Require(code, Helpers, "BongBongPlace", typeof(bool), true,
                typeof(int), typeof(int), typeof(Item), typeof(Player), typeof(bool), typeof(bool), typeof(bool)),
                (Func<Func<int, int, Item, Player, bool, bool, bool, bool>, int, int, Item, Player, bool, bool, bool, bool>)
                ((orig, x, y, item, player, mute, forced, sound) => Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Replace,
                    () => orig(x, y, item, player, mute, forced, sound)))));
            group.Add(new Hook(Require(code, Helpers, "PlaceChestNoSync", typeof(int), true,
                typeof(int), typeof(int), typeof(ushort), typeof(bool), typeof(int)),
                (Func<Func<int, int, ushort, bool, int, int>, int, int, ushort, bool, int, int>)
                ((orig, x, y, type, near, style) => Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Place,
                    () => orig(x, y, type, near, style)))));
            group.Add(new Hook(Require(code, Helpers, "Place3x2NoSyncDresser", typeof(bool), true,
                typeof(int), typeof(int), typeof(ushort), typeof(int)),
                (Func<Func<int, int, ushort, int, bool>, int, int, ushort, int, bool>)
                ((orig, x, y, type, style) => Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Place,
                    () => orig(x, y, type, style)))));
            group.Add(new Hook(Require(code, Helpers, "TryPlaceMultiTileDirect", typeof(bool), true,
                typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(TileObject).MakeByRefType()),
                (PlaceObjectHook)HandleObject));
        }

        /// <summary>
        /// 在单格施工的材料搜索前捕获明确目标；上游可能在搜索失败后仍直接还原草类型，
        /// 因此不能要求 TryPlaceTile 确实被调用才观察该格。闭包字段在能力安装时已验证。
        /// </summary>
        private static Item HandleMaterialSearch(Func<Player, Item[], Func<Item, bool>, bool, Item> orig,
            Player player, Item[] items, Func<Item, bool> predicate, bool consume)
        {
            if (activeWork?.CanRecord == true && activeWork.Phase == "GenerateSingleTiles"
                && singleTilePosition?.DeclaringType.IsInstanceOfType(predicate?.Target) == true)
            {
                Point point = (Point)singleTilePosition.GetValue(predicate.Target);
                TileChangeScope.Observe(point.X, point.Y, TileChangeLayer.Tile, TileChangeIntent.Place, direct: true);
            }
            return orig(player, items, predicate, consume);
        }

        /// <summary>保持直接家具放置的 out 结果，不以返回值替代完整对象状态验证。</summary>
        private static bool HandleObject(PlaceObject orig, int x, int y, int type, int style, int direction, out TileObject data)
        {
            if (activeWork?.CanRecord == true)
                TileChangeScope.Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Place, direct: true);
            ImproveGameMultiplayerSystem.FlushClientRemovals();
            return orig(x, y, type, style, direction, out data);
        }

        /// <summary>只注册受信辅助入口的直接目标；构造时保留到本次 MoveNext 结束以读取草还原后的类型。</summary>
        private static T Observe<T>(int x, int y, TileChangeLayer layer, TileChangeIntent intent, Func<T> action)
        {
            if (activeWork?.CanRecord == true)
                TileChangeScope.Observe(x, y, layer, intent, direct: true);
            mutationDepth++;
            try
            {
                T result = action();
                ImproveGameMultiplayerSystem.FlushClientRemovals();
                return result;
            }
            finally { mutationDepth--; }
        }

        /// <summary>构造清理直接调用 KillWall；递归环境连锁和其他阶段不提供额外归属。</summary>
        private static void HandleKillWall(On_WorldGen.orig_KillWall orig, int x, int y, bool fail)
        {
            if (activeWork?.CanRecord == true && activeWork.Phase == "KillTiles" && mutationDepth == 0)
                TileChangeScope.Observe(x, y, TileChangeLayer.Wall, TileChangeIntent.Remove, direct: true);
            mutationDepth++;
            try { orig(x, y, fail); }
            finally { mutationDepth--; TileChangeScope.NoteRemoval(x, y, TileChangeLayer.Wall); }
        }

        /// <summary>
        /// 在实际破坏前观察显式清理目标，包括被前一目标联动提前拆掉的后续目标。
        /// 若等到工具遍历到后续目标才快照，格位已空而永久漏计；区域外变化仍不归属。
        /// </summary>
        private static void HandleKillTile(On_WorldGen.orig_KillTile orig, int x, int y,
            bool fail, bool effectOnly, bool noItem)
        {
            if (activeWork?.CanRecord == true && (activeWork.Phase == "KillTiles" || activeWork.Phase == "Building")
                && activeWork.Cleanup?.Contains(x, y) == true)
                TileChangeScope.Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Remove, direct: true);
            orig(x, y, fail, effectOnly, noItem);
            TileChangeScope.NoteRemoval(x, y, TileChangeLayer.Tile);
        }

        /// <summary>观察附加组件阶段的一次实际放线/制动器目标。</summary>
        private static void ObserveWire(int x, int y)
        {
            if (activeWork?.CanRecord == true && activeWork.Phase == "GenerateOutSet")
                TileChangeScope.Observe(x, y, TileChangeLayer.Wiring, TileChangeIntent.Place, direct: true);
        }

        /// <summary>观察红线。</summary>
        private static bool HandleRedWire(On_WorldGen.orig_PlaceWire orig, int x, int y) { ObserveWire(x, y); return orig(x, y); }
        /// <summary>观察蓝线。</summary>
        private static bool HandleBlueWire(On_WorldGen.orig_PlaceWire2 orig, int x, int y) { ObserveWire(x, y); return orig(x, y); }
        /// <summary>观察绿线。</summary>
        private static bool HandleGreenWire(On_WorldGen.orig_PlaceWire3 orig, int x, int y) { ObserveWire(x, y); return orig(x, y); }
        /// <summary>观察黄线。</summary>
        private static bool HandleYellowWire(On_WorldGen.orig_PlaceWire4 orig, int x, int y) { ObserveWire(x, y); return orig(x, y); }
        /// <summary>观察制动器组件，激活状态切换不经过此入口。</summary>
        private static bool HandleActuator(On_WorldGen.orig_PlaceActuator orig, int x, int y) { ObserveWire(x, y); return orig(x, y); }

        /// <summary>清除当前同步上下文，保留弱关联的旧会话标记直到迭代器回收或 Hook 卸载。</summary>
        private static void ClearTasks()
        {
            // 换世界仍保留弱关联中的旧会话标记，使尚未被上游取消的旧任务只能在抑制作用域中继续。
            // 弱键不会阻止取消任务回收，只有卸载并移除全部 Hook 后才整体清空。
            activeWork = null;
            launchPlayer = null;
            mutationDepth = 0;
        }

        /// <summary>保存不可变的任务所有者与世界会话；每个子阶段共享身份但分别结算。</summary>
        private sealed class Work
        {
            /// <summary>从真实发起入口绑定当前世界会话。</summary>
            internal Work(Player player, string phase) : this(player, phase, TileChangeScope.WorldSession) { }
            /// <summary>构造同一任务的子阶段，保留原始会话。</summary>
            internal Work(Player player, string phase, long session, CleanupArea? cleanup = null)
            { Player = player; Phase = phase; Session = session; Cleanup = cleanup; }
            internal Player Player { get; }
            internal string Phase { get; }
            internal bool IsMutationPhase => Phase == "KillTiles" || Phase == "GenerateWalls"
                || Phase == "GenerateSingleTiles" || Phase == "GenerateMultiTiles" || Phase == "GenerateOutSet";
            private long Session { get; }
            internal CleanupArea? Cleanup { get; }
            internal bool CanRecord => !Main.gameMenu
                && Main.myPlayer >= 0 && Main.myPlayer < Main.player.Length
                && Session == TileChangeScope.WorldSession && Player?.active == true
                && Player.whoAmI == Main.myPlayer && Main.player[Main.myPlayer] == Player
                && (Main.netMode == NetmodeID.SinglePlayer || ImproveGameMultiplayerSystem.CanUseClient(Player,
                    Phase == "Building" ? ImproveGameCapability.Building : ImproveGameCapability.Construct));
            /// <summary>把根任务身份传播到返回的子迭代器。</summary>
            internal Work ForPhase(string phase, CleanupArea? cleanup = null) => new Work(Player, phase, Session, cleanup);
        }

        /// <summary>保存清理闭区间及可选的建筑模板目标掩码；构造尺寸在上游表示最大偏移。</summary>
        private readonly struct CleanupArea
        {
            private readonly int left, top;
            private readonly long right, bottom;
            private readonly bool valid;
            private readonly bool[] mask;
            private readonly int stride;
            /// <summary>按上游包含边界的循环保存范围，用 long 防止坐标加法溢出。</summary>
            internal CleanupArea(Point origin, int width, int height, bool[] mask = null)
            { left = origin.X; top = origin.Y; right = (long)origin.X + width; bottom = (long)origin.Y + height;
                valid = width >= 0 && height >= 0; this.mask = mask; stride = width + 1; }
            /// <summary>只接受明确清理范围内的格位，不扩张到附近环境。</summary>
            internal bool Contains(int x, int y) => valid && x >= left && x <= right && y >= top && y <= bottom
                && (mask == null || mask[(int)(((long)y - top) * stride + (long)x - left)]);
        }
    }
}

