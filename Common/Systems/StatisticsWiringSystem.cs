using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 将本地玩家的普通和批量布线流程与 WorldGen 的实际格位变化关联，
    /// 分别记录四种电线及制动器的成功放置和拆除数量。
    /// </summary>
    internal sealed class StatisticsWiringSystem : ModSystem
    {
        // WorldGen 的布线方法本身没有玩家参数。只有它们同步嵌套在本地玩家的
        // ItemCheck_UseWiringTools 调用中时才允许归因，网络同步和世界逻辑因此被排除。
        private static Player activeLocalWiringPlayer;
        private static int massWiringStepDepth;

        public override void Load()
        {
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
            massWiringStepDepth = 0;
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
            massWiringStepDepth++;
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
                massWiringStepDepth--;
            }

            if (ownsOperation)
            {
                RecordComponentMaskChanges(
                    player,
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
            activeLocalWiringPlayer = ownsOperation ? player : null;

            try
            {
                orig(player, item);
            }
            finally
            {
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
                || player == null
                || !WorldGen.InWorld(x, y))
            {
                return;
            }

            bool after = GetComponentState(x, y, component);
            if (before == after)
            {
                return;
            }

            player.GetModPlayer<StatisticsPlayer>()
                .RecordWiringComponentChange(component, placed: after);
        }

        /// <summary>
        /// 将宏伟蓝图单格处理前后发生变化的每一种组件分别提交一次。
        /// </summary>
        private static void RecordComponentMaskChanges(
            Player player,
            int beforeMask,
            int afterMask)
        {
            int changedMask = beforeMask ^ afterMask;
            if (changedMask == 0)
            {
                return;
            }

            StatisticsPlayer statisticsPlayer = player.GetModPlayer<StatisticsPlayer>();
            foreach (WiringComponentKind component in System.Enum.GetValues<WiringComponentKind>())
            {
                int componentMask = 1 << (int)component;
                if ((changedMask & componentMask) != 0)
                {
                    statisticsPlayer.RecordWiringComponentChange(
                        component,
                        placed: (afterMask & componentMask) != 0);
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
        /// 在本地玩家布线作用域内读取一种组件的独立存在状态；其他来源直接返回 false，
        /// 避免世界生成和网络同步的布线调用产生额外格位读取开销。
        /// </summary>
        private static bool GetComponentState(int x, int y, WiringComponentKind component)
        {
            if (massWiringStepDepth > 0
                || activeLocalWiringPlayer == null
                || !WorldGen.InWorld(x, y))
            {
                return false;
            }

            Tile tile = Framing.GetTileSafely(x, y);
            return component switch
            {
                WiringComponentKind.RedWire => tile.RedWire,
                WiringComponentKind.BlueWire => tile.BlueWire,
                WiringComponentKind.GreenWire => tile.GreenWire,
                WiringComponentKind.YellowWire => tile.YellowWire,
                WiringComponentKind.Actuator => tile.HasActuator,
                _ => false
            };
        }
    }
}
