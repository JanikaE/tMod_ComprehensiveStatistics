using System.Collections.Generic;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 将本地玩家的普通和批量布线候选与服务器观察到的 WorldGen 实际变化关联，
    /// 分别记录四种电线及制动器经权威端确认的成功放置和拆除数量。
    /// </summary>
    internal sealed class StatisticsWiringSystem : ModSystem
    {
        private const int WiringComponentCount = 5;

        // WorldGen 的布线方法本身没有玩家参数。本地调用提供候选变化；服务器入站
        // MessageBuffer 上下文提供权威发送者，二者逐格匹配后才允许归入人物统计。
        private static Player activeLocalWiringPlayer;
        private static Player wiringBatchOwner;
        private static int massWiringStepDepth;
        private static int activeServerMessagePlayer = -1;
        private static readonly long[] pendingPlacedComponents = new long[WiringComponentCount];
        private static readonly long[] pendingRemovedComponents = new long[WiringComponentCount];
        private static readonly List<WiringChangeVerificationEntry> pendingVerificationChanges =
            new List<WiringChangeVerificationEntry>();
        private static readonly object serverChangesLock = new object();
        private static Dictionary<
            (int X, int Y, WiringComponentKind Component, bool Present),
            int>[] serverAcceptedChanges;
        private static ulong[] serverAcceptedChangeUpdates;

        public override void Load()
        {
            On_MessageBuffer.GetData += HandleNetworkMessage;
            On_Player.ItemCheck_UseWiringTools += HandleUseWiringTools;
            On_Wiring.MassWireOperationStep += HandleMassWireOperationStep;
            On_WorldGen.PlaceWire += HandlePlaceRedWire;
            On_WorldGen.PlaceWire2 += HandlePlaceBlueWire;
            On_WorldGen.PlaceWire3 += HandlePlaceGreenWire;
            On_WorldGen.PlaceWire4 += HandlePlaceYellowWire;
            On_WorldGen.PlaceActuator += HandlePlaceActuator;
            On_WorldGen.KillWire += HandleRemoveRedWire;
            On_WorldGen.KillWire2 += HandleRemoveBlueWire;
            On_WorldGen.KillWire3 += HandleRemoveGreenWire;
            On_WorldGen.KillWire4 += HandleRemoveYellowWire;
            On_WorldGen.KillActuator += HandleRemoveActuator;
        }

        public override void Unload()
        {
            On_MessageBuffer.GetData -= HandleNetworkMessage;
            On_Player.ItemCheck_UseWiringTools -= HandleUseWiringTools;
            On_Wiring.MassWireOperationStep -= HandleMassWireOperationStep;
            On_WorldGen.PlaceWire -= HandlePlaceRedWire;
            On_WorldGen.PlaceWire2 -= HandlePlaceBlueWire;
            On_WorldGen.PlaceWire3 -= HandlePlaceGreenWire;
            On_WorldGen.PlaceWire4 -= HandlePlaceYellowWire;
            On_WorldGen.PlaceActuator -= HandlePlaceActuator;
            On_WorldGen.KillWire -= HandleRemoveRedWire;
            On_WorldGen.KillWire2 -= HandleRemoveBlueWire;
            On_WorldGen.KillWire3 -= HandleRemoveGreenWire;
            On_WorldGen.KillWire4 -= HandleRemoveYellowWire;
            On_WorldGen.KillActuator -= HandleRemoveActuator;
            activeLocalWiringPlayer = null;
            wiringBatchOwner = null;
            massWiringStepDepth = 0;
            activeServerMessagePlayer = -1;
            serverAcceptedChanges = null;
            serverAcceptedChangeUpdates = null;
            ClearPendingWiringChanges();
        }

        public override void OnWorldLoad()
        {
            ResetServerAcceptedChanges();
        }

        public override void OnWorldUnload()
        {
            ResetServerAcceptedChanges();
        }

        public override void PostUpdateWorld()
        {
            if (Main.netMode != NetmodeID.Server
                || serverAcceptedChanges == null
                || serverAcceptedChangeUpdates == null
                || Main.GameUpdateCount % 60UL != 0UL)
            {
                return;
            }

            lock (serverChangesLock)
            {
                for (int playerIndex = 0;
                    playerIndex < serverAcceptedChanges.Length;
                    playerIndex++)
                {
                    if (Main.player[playerIndex]?.active == true
                        && Main.GameUpdateCount - serverAcceptedChangeUpdates[playerIndex]
                            <= 300UL)
                    {
                        continue;
                    }

                    serverAcceptedChanges[playerIndex]?.Clear();
                }
            }
        }

        /// <summary>
        /// 在服务器处理某个客户端的原版入站消息期间保留发送者上下文，
        /// 使 WorldGen 布线入口的真实状态差可以回归到唯一发起玩家。
        /// </summary>
        private static void HandleNetworkMessage(
            On_MessageBuffer.orig_GetData orig,
            MessageBuffer self,
            int start,
            int length,
            out int messageType)
        {
            int previousPlayer = activeServerMessagePlayer;
            bool ownsServerMessage = Main.netMode == NetmodeID.Server
                && self != null
                && self.whoAmI >= 0
                && self.whoAmI < Main.maxPlayers
                && Main.player[self.whoAmI]?.active == true;
            activeServerMessagePlayer = ownsServerMessage ? self.whoAmI : -1;

            try
            {
                orig(self, start, length, out messageType);
            }
            finally
            {
                activeServerMessagePlayer = previousPlayer;
            }
        }

        /// <summary>
        /// 直接观察宏伟蓝图逐格处理前后的全部布线状态。宏伟蓝图在此方法内调用
        /// WorldGen 的普通布线入口，因此处理期间暂停普通入口计数，避免同一变化重复提交。
        /// </summary>
        private static bool? HandleMassWireOperationStep(
            On_Wiring.orig_MassWireOperationStep orig,
            Player player,
            Point position,
            WiresUI.Settings.MultiToolMode mode,
            ref int wireCount,
            ref int actuatorCount)
        {
            bool ownsOperation = !Main.dedServ
                && !Main.gameMenu
                && player?.active == true
                && player.whoAmI == Main.myPlayer
                && WorldGen.InWorld(position.X, position.Y);
            int beforeMask = ownsOperation
                ? GetComponentMask(position.X, position.Y)
                : 0;

            bool? result;
            if (ownsOperation)
            {
                massWiringStepDepth++;
            }

            try
            {
                result = orig(
                    player,
                    position,
                    mode,
                    ref wireCount,
                    ref actuatorCount);
            }
            finally
            {
                if (ownsOperation)
                {
                    massWiringStepDepth--;
                }
            }

            if (ownsOperation)
            {
                RecordComponentMaskChanges(
                    player,
                    position.X,
                    position.Y,
                    beforeMask,
                    GetComponentMask(position.X, position.Y));
            }

            return result;
        }

        /// <summary>
        /// 在玩家布线调用的同步作用域中保存归因上下文；宏伟蓝图的 MassWireOperation
        /// 也在该调用内逐格进入 WorldGen，因此无需按拖动手势猜测改变数量。
        /// </summary>
        private static void HandleUseWiringTools(
            On_Player.orig_ItemCheck_UseWiringTools orig,
            Player player,
            Item item)
        {
            Player previousPlayer = activeLocalWiringPlayer;
            bool ownsOperation = !Main.dedServ
                && !Main.gameMenu
                && player?.active == true
                && player.whoAmI == Main.myPlayer;
            bool startsBatch = ownsOperation && wiringBatchOwner == null;
            if (startsBatch)
            {
                wiringBatchOwner = player;
                ClearPendingWiringChanges();
            }

            activeLocalWiringPlayer = ownsOperation ? player : null;

            try
            {
                orig(player, item);
            }
            finally
            {
                if (startsBatch)
                {
                    FlushPendingWiringChanges(player);
                    wiringBatchOwner = null;
                }

                activeLocalWiringPlayer = previousPlayer;
            }
        }

        /// <summary>
        /// 观察红电线放置调用前后的格位状态。
        /// </summary>
        private static bool HandlePlaceRedWire(
            On_WorldGen.orig_PlaceWire orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.RedWire);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.RedWire, before);
            return result;
        }

        /// <summary>
        /// 观察蓝电线放置调用前后的格位状态。
        /// </summary>
        private static bool HandlePlaceBlueWire(
            On_WorldGen.orig_PlaceWire2 orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.BlueWire);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.BlueWire, before);
            return result;
        }

        /// <summary>
        /// 观察绿电线放置调用前后的格位状态。
        /// </summary>
        private static bool HandlePlaceGreenWire(
            On_WorldGen.orig_PlaceWire3 orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.GreenWire);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.GreenWire, before);
            return result;
        }

        /// <summary>
        /// 观察黄电线放置调用前后的格位状态。
        /// </summary>
        private static bool HandlePlaceYellowWire(
            On_WorldGen.orig_PlaceWire4 orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.YellowWire);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.YellowWire, before);
            return result;
        }

        /// <summary>
        /// 观察制动器放置调用前后的格位状态。
        /// </summary>
        private static bool HandlePlaceActuator(
            On_WorldGen.orig_PlaceActuator orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.Actuator);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.Actuator, before);
            return result;
        }

        /// <summary>
        /// 观察红电线拆除调用前后的格位状态。
        /// </summary>
        private static bool HandleRemoveRedWire(
            On_WorldGen.orig_KillWire orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.RedWire);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.RedWire, before);
            return result;
        }

        /// <summary>
        /// 观察蓝电线拆除调用前后的格位状态。
        /// </summary>
        private static bool HandleRemoveBlueWire(
            On_WorldGen.orig_KillWire2 orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.BlueWire);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.BlueWire, before);
            return result;
        }

        /// <summary>
        /// 观察绿电线拆除调用前后的格位状态。
        /// </summary>
        private static bool HandleRemoveGreenWire(
            On_WorldGen.orig_KillWire3 orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.GreenWire);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.GreenWire, before);
            return result;
        }

        /// <summary>
        /// 观察黄电线拆除调用前后的格位状态。
        /// </summary>
        private static bool HandleRemoveYellowWire(
            On_WorldGen.orig_KillWire4 orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.YellowWire);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.YellowWire, before);
            return result;
        }

        /// <summary>
        /// 观察制动器拆除调用前后的格位状态。
        /// </summary>
        private static bool HandleRemoveActuator(
            On_WorldGen.orig_KillActuator orig,
            int x,
            int y)
        {
            bool before = GetComponentState(x, y, WiringComponentKind.Actuator);
            bool result = orig(x, y);
            RecordChangedComponent(x, y, WiringComponentKind.Actuator, before);
            return result;
        }

        /// <summary>
        /// 比较调用前后的真实格位状态；重复放置、空拆、权限失败和制动器切换均不会提交。
        /// </summary>
        private static void RecordChangedComponent(
            int x,
            int y,
            WiringComponentKind component,
            bool before)
        {
            Player player = activeLocalWiringPlayer;
            if (massWiringStepDepth > 0
                || (player == null && activeServerMessagePlayer < 0)
                || !WorldGen.InWorld(x, y))
            {
                return;
            }

            bool after = GetComponentState(x, y, component);
            if (before == after)
            {
                return;
            }

            if (player != null)
            {
                AccumulateOrRecordComponentChange(
                    player,
                    x,
                    y,
                    component,
                    placed: after);
            }
            else
            {
                RecordServerAcceptedChange(
                    activeServerMessagePlayer,
                    x,
                    y,
                    component,
                    present: after);
            }
        }

        /// <summary>
        /// 将宏伟蓝图单格处理前后发生变化的每一种组件分别提交一次。
        /// </summary>
        private static void RecordComponentMaskChanges(
            Player player,
            int x,
            int y,
            int beforeMask,
            int afterMask)
        {
            int changedMask = beforeMask ^ afterMask;
            if (changedMask == 0)
            {
                return;
            }

            for (int componentIndex = 0;
                componentIndex < WiringComponentCount;
                componentIndex++)
            {
                WiringComponentKind component = (WiringComponentKind)componentIndex;
                int componentMask = 1 << (int)component;
                if ((changedMask & componentMask) != 0)
                {
                    AccumulateOrRecordComponentChange(
                        player,
                        x,
                        y,
                        component,
                        placed: (afterMask & componentMask) != 0);
                }
            }
        }

        /// <summary>
        /// 在一次玩家布线调用内累计变化；多人客户端保留逐格候选，单人使用定长数组。
        /// </summary>
        private static void AccumulateOrRecordComponentChange(
            Player player,
            int x,
            int y,
            WiringComponentKind component,
            bool placed)
        {
            int componentIndex = (int)component;
            if (ReferenceEquals(player, wiringBatchOwner)
                && componentIndex >= 0
                && componentIndex < WiringComponentCount)
            {
                long[] pendingCounts = placed
                    ? pendingPlacedComponents
                    : pendingRemovedComponents;
                if (pendingCounts[componentIndex] < long.MaxValue)
                {
                    pendingCounts[componentIndex]++;
                }

                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    pendingVerificationChanges.Add(
                        new WiringChangeVerificationEntry(
                            x,
                            y,
                            component,
                            expectedPresent: placed));
                }

                return;
            }

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                StatisticsNetcode.RequestWiringChangeVerification(
                    new[]
                    {
                        new WiringChangeVerificationEntry(
                            x,
                            y,
                            component,
                            expectedPresent: placed)
                    });
            }
            else
            {
                player.GetModPlayer<StatisticsPlayer>()
                    .RecordWiringComponentChanges(component, placed, count: 1L);
            }
        }

        /// <summary>
        /// 结束一次布线工具调用：单人提交分类数量，多人将逐格候选发给服务器验证。
        /// </summary>
        private static void FlushPendingWiringChanges(Player player)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                StatisticsNetcode.RequestWiringChangeVerification(
                    pendingVerificationChanges);
            }
            else
            {
                StatisticsPlayer statisticsPlayer = player.GetModPlayer<StatisticsPlayer>();
                for (int componentIndex = 0;
                    componentIndex < WiringComponentCount;
                    componentIndex++)
                {
                    WiringComponentKind component = (WiringComponentKind)componentIndex;
                    statisticsPlayer.RecordWiringComponentChanges(
                        component,
                        placed: true,
                        pendingPlacedComponents[componentIndex]);
                    statisticsPlayer.RecordWiringComponentChanges(
                        component,
                        placed: false,
                        pendingRemovedComponents[componentIndex]);
                }
            }

            ClearPendingWiringChanges();
        }

        /// <summary>
        /// 清空定长布线批次，避免热重载或嵌套调用留下旧数量。
        /// </summary>
        private static void ClearPendingWiringChanges()
        {
            System.Array.Clear(pendingPlacedComponents, 0, pendingPlacedComponents.Length);
            System.Array.Clear(pendingRemovedComponents, 0, pendingRemovedComponents.Length);
            pendingVerificationChanges.Clear();
        }

        /// <summary>
        /// 在服务器或单人权威世界读取指定格位组件的最终状态，供多人布线确认使用。
        /// </summary>
        internal static bool TryGetAuthoritativeComponentState(
            int x,
            int y,
            WiringComponentKind component,
            out bool present)
        {
            present = false;
            if (!WorldGen.InWorld(x, y)
                || (int)component < 0
                || (int)component >= WiringComponentCount)
            {
                return false;
            }

            Tile tile = Framing.GetTileSafely(x, y);
            present = component switch
            {
                WiringComponentKind.RedWire => tile.RedWire,
                WiringComponentKind.BlueWire => tile.BlueWire,
                WiringComponentKind.GreenWire => tile.GreenWire,
                WiringComponentKind.YellowWire => tile.YellowWire,
                WiringComponentKind.Actuator => tile.HasActuator,
                _ => false
            };
            return true;
        }

        /// <summary>
        /// 消耗服务器在该玩家入站消息中实际观察到的一次匹配布线变化；
        /// 没有真实前后差值或已经确认过的候选不会再次通过。
        /// </summary>
        internal static bool ConsumeServerAcceptedChange(
            int playerIndex,
            int x,
            int y,
            WiringComponentKind component,
            bool present)
        {
            if (Main.netMode != NetmodeID.Server
                || serverAcceptedChanges == null
                || playerIndex < 0
                || playerIndex >= serverAcceptedChanges.Length)
            {
                return false;
            }

            (int X, int Y, WiringComponentKind Component, bool Present) key =
                (x, y, component, present);
            lock (serverChangesLock)
            {
                Dictionary<
                    (int X, int Y, WiringComponentKind Component, bool Present),
                    int> changes = serverAcceptedChanges[playerIndex];
                if (changes == null
                    || !changes.TryGetValue(key, out int remaining)
                    || remaining <= 0)
                {
                    return false;
                }

                if (remaining == 1)
                {
                    changes.Remove(key);
                }
                else
                {
                    changes[key] = remaining - 1;
                }

                return true;
            }
        }

        /// <summary>
        /// 保存服务器在客户端原版消息中确认的真实组件差值，等待对应候选包逐项领取。
        /// </summary>
        private static void RecordServerAcceptedChange(
            int playerIndex,
            int x,
            int y,
            WiringComponentKind component,
            bool present)
        {
            if (Main.netMode != NetmodeID.Server
                || serverAcceptedChanges == null
                || playerIndex < 0
                || playerIndex >= serverAcceptedChanges.Length)
            {
                return;
            }

            (int X, int Y, WiringComponentKind Component, bool Present) key =
                (x, y, component, present);
            lock (serverChangesLock)
            {
                Dictionary<
                    (int X, int Y, WiringComponentKind Component, bool Present),
                    int> changes = serverAcceptedChanges[playerIndex] ??=
                        new Dictionary<
                            (int X, int Y, WiringComponentKind Component, bool Present),
                            int>();
                changes.TryGetValue(key, out int existingCount);
                if (existingCount < int.MaxValue)
                {
                    changes[key] = existingCount + 1;
                }

                serverAcceptedChangeUpdates[playerIndex] = Main.GameUpdateCount;
            }
        }

        /// <summary>
        /// 为当前世界建立空的服务器确认缓存；该缓存从不跨世界或热重载复用。
        /// </summary>
        private static void ResetServerAcceptedChanges()
        {
            lock (serverChangesLock)
            {
                if (Main.netMode == NetmodeID.Server)
                {
                    serverAcceptedChanges = new Dictionary<
                        (int X, int Y, WiringComponentKind Component, bool Present),
                        int>[Main.maxPlayers];
                    serverAcceptedChangeUpdates = new ulong[Main.maxPlayers];
                }
                else
                {
                    serverAcceptedChanges = null;
                    serverAcceptedChangeUpdates = null;
                }
            }
        }

        /// <summary>
        /// 将一个格位上的四种电线与制动器状态压缩为位掩码，供批量布线前后比较。
        /// </summary>
        private static int GetComponentMask(int x, int y)
        {
            Tile tile = Framing.GetTileSafely(x, y);
            int mask = 0;
            mask |= tile.RedWire ? 1 << (int)WiringComponentKind.RedWire : 0;
            mask |= tile.BlueWire ? 1 << (int)WiringComponentKind.BlueWire : 0;
            mask |= tile.GreenWire ? 1 << (int)WiringComponentKind.GreenWire : 0;
            mask |= tile.YellowWire ? 1 << (int)WiringComponentKind.YellowWire : 0;
            mask |= tile.HasActuator ? 1 << (int)WiringComponentKind.Actuator : 0;
            return mask;
        }

        /// <summary>
        /// 在本地玩家工具作用域或服务器玩家入站消息作用域内读取组件状态；
        /// 其他来源直接返回 false，避免世界生成和普通网络同步产生额外读取与归因。
        /// </summary>
        private static bool GetComponentState(int x, int y, WiringComponentKind component)
        {
            if (massWiringStepDepth > 0
                || (activeLocalWiringPlayer == null && activeServerMessagePlayer < 0)
                || !WorldGen.InWorld(x, y))
            {
                return false;
            }

            return TryGetAuthoritativeComponentState(
                x,
                y,
                component,
                out bool present)
                && present;
        }
    }
}
