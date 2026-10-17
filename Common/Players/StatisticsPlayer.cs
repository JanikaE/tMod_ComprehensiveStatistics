using System;
using System.Collections.Generic;
using ComprehensiveStatistics.Common.Statistics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ComprehensiveStatistics.Common.Players
{
    /// <summary>
    /// 拥有角色统计数据，并为采集器提供同时写入生涯与当前世界切片的统一入口。
    /// </summary>
    internal sealed class StatisticsPlayer : ModPlayer
    {
        // 原版正常移动单帧不会跨越四格；更大的跳变视为传送或位置校正。
        // 显式的 teleportTime 检查负责排除短距离传送，此阈值是兼容未知传送实现的兜底。
        private const float MaximumTrackedMovementPerTick = 64f;

        // 顺序是设计规定的归因优先级；静态数组避免在逐帧热路径中反复分配。
        private static readonly MovementModeKind[] SpecialMovementModePriority =
        {
            MovementModeKind.Minecart,
            MovementModeKind.OtherMount,
            MovementModeKind.RopeOrPulley,
            MovementModeKind.GrapplingHook,
            MovementModeKind.Flying
        };

        private Guid currentWorldId;
        private StatisticsData currentWorldSlice;
        private bool hasRecordedCurrentDeath;
        private bool hasLifeSnapshot;
        private int previousLifeSnapshot;
        private long pendingDirectLifeLost;
        private bool hasPendingDirectDeathSource;
        private DirectDamageSourceKind pendingDirectDeathSource;
        private StableContentKey? pendingDirectDeathNpc;
        private StatisticKey? pendingContinuousDeathSource;
        private bool hasMovementSnapshot;
        private Vector2 previousMovementPosition;
        private MovementModeKind previousMovementMode;
        private bool hasPreMovementMode;
        private MovementModeKind preMovementMode;
        private StableContentKey? preMovementContent;
        private double pendingMovementPixels;
        private bool hasActiveChanneledItemUse;
        private int activeChanneledItemType;
        private int activeChanneledAltFunctionUse;
        private readonly List<PendingPickupObservation> pendingPickupObservations =
            new List<PendingPickupObservation>();

        internal CharacterStatisticsData Data { get; private set; }

        internal StatisticsData CurrentWorldSlice => currentWorldSlice;

        public override void Initialize()
        {
            Data = new CharacterStatisticsData(DateTimeOffset.UtcNow);
            currentWorldId = Guid.Empty;
            currentWorldSlice = null;
            hasRecordedCurrentDeath = false;
            hasLifeSnapshot = false;
            previousLifeSnapshot = 0;
            pendingDirectLifeLost = 0L;
            ClearPendingDeathSources();
            ResetMovementTracking();
            ResetItemUseTracking();
            pendingPickupObservations.Clear();
        }

        public override void OnEnterWorld()
        {
            EnsureCurrentWorldSlice();
            hasRecordedCurrentDeath = false;
            ResetLifeLossTracking();
            ResetMovementTracking();
            ResetItemUseTracking();
            pendingPickupObservations.Clear();

            // B2 临时验收计数只由所属客户端写入，进入世界一次即同时增长两个角色范围。
            if (IsOwnedByLocalClient)
            {
                Record(InternalStatisticKeys.B2WorldEntries, new StatisticMetrics(events: 1L));
            }
        }

        public override void SaveData(TagCompound tag)
        {
            tag[StatisticsDataSerializer.PlayerTagKey] = StatisticsDataSerializer.Serialize(Data);
        }

        public override void LoadData(TagCompound tag)
        {
            if (StatisticsDataSerializer.TryDeserialize(
                tag,
                out CharacterStatisticsData loadedData,
                out string error))
            {
                Data = loadedData;
            }
            else if (!string.IsNullOrEmpty(error))
            {
                Mod.Logger.Warn($"Ignoring invalid character statistics data. {error}");
            }

            // OnEnterWorld 会根据当前世界 UUID 重新连接到已加载的对应切片。
            currentWorldId = Guid.Empty;
            currentWorldSlice = null;
            hasRecordedCurrentDeath = false;
            hasLifeSnapshot = false;
            previousLifeSnapshot = 0;
            pendingDirectLifeLost = 0L;
            ClearPendingDeathSources();
            ResetMovementTracking();
            ResetItemUseTracking();
            pendingPickupObservations.Clear();
        }

        public override void Kill(
            double damage,
            int hitDirection,
            bool pvp,
            PlayerDeathReason damageSource)
        {
            // Kill 会在本地、服务器和远程客户端触发。个人统计只由死亡玩家所属客户端提交，
            // 从源头避免同一次死亡因网络副本重复累计。
            if (!IsOwnedByLocalClient)
            {
                return;
            }

            // 持续生命流失致死后不一定还有完整的 PostUpdate，因此先结算最后一段生命。
            ReconcileLifeLoss();
            if (hasRecordedCurrentDeath)
            {
                return;
            }

            hasRecordedCurrentDeath = true;
            Record(StatisticKeys.PlayerDeaths, new StatisticMetrics(events: 1L));
            RecordDeathSource(damageSource, pvp);
            ClearPendingDeathSources();
        }

        public override void OnHurt(Player.HurtInfo info)
        {
            // OnHurt 只在受伤玩家的本地客户端触发，且此时 statLife 仍是扣血前的值。
            // HurtInfo.Damage 是最终伤害但不按剩余生命封顶，因此致死伤害必须在此截断。
            if (!IsOwnedByLocalClient)
            {
                return;
            }

            int actualLifeLost = Math.Min(Math.Max(Player.statLife, 0), info.Damage);
            if (actualLifeLost > 0)
            {
                EnsureLifeSnapshot();
                pendingDirectLifeLost = SaturatingAdd(
                    pendingDirectLifeLost,
                    actualLifeLost);

                DirectDamageSourceKind source = DirectDamageSourceClassifier.Classify(
                    info,
                    out NPC npcSource);
                StableContentKey? npcKey = null;
                Record(
                    StatisticKeys.DirectLifeLost,
                    new StatisticMetrics(amount: actualLifeLost));
                Record(
                    StatisticKeys.GetDirectDamageSourceKey(source),
                    new StatisticMetrics(amount: actualLifeLost));

                if (npcSource != null
                    && StableContentKey.TryCreateForNpc(
                        npcSource,
                        out StableContentKey resolvedNpcKey))
                {
                    npcKey = resolvedNpcKey;
                    Record(
                        StatisticKeys.GetNpcDirectDamageKey(resolvedNpcKey),
                        new StatisticMetrics(amount: actualLifeLost));
                }

                if (actualLifeLost >= Math.Max(Player.statLife, 0))
                {
                    hasPendingDirectDeathSource = true;
                    pendingDirectDeathSource = source;
                    pendingDirectDeathNpc = npcKey;
                    pendingContinuousDeathSource = null;
                }
            }
        }

        public override void PostUpdate()
        {
            if (IsOwnedByLocalClient)
            {
                ReconcileItemPickups();
                ReconcileLifeLoss();
                TrackMovementDistance();
                UpdateItemUseTracking();
            }
        }

        /// <summary>
        /// 保存全局物品回调观察到的拾取前状态，延迟到原版入包逻辑完成后结算。
        /// </summary>
        internal void ObserveItemPickup(Item item)
        {
            if (!IsOwnedByLocalClient
                || item == null
                || item.stack <= 0
                || !StableContentKey.TryCreateForItem(
                    item.type,
                    out StableContentKey content))
            {
                return;
            }

            // GlobalItem.OnPickup 位于原版实际入包逻辑之前；这里只保存世界物品状态，等 PostUpdate
            // 根据世界槽位的剩余堆叠结算，才能排除背包已满时反复触发的无效拾取，
            // 并准确处理一次只吸收部分堆叠的情况。
            pendingPickupObservations.Add(new PendingPickupObservation(
                item,
                item.whoAmI,
                item.type,
                item.stack,
                content));
        }

        /// <summary>
        /// 为本地所属角色记录一次已经确认发生的物品使用。
        /// </summary>
        internal void RecordItemUse(Item item)
        {
            if (!IsOwnedByLocalClient
                || item == null
                || item.IsAir
                || !StableContentKey.TryCreateForItem(
                    item.type,
                    out StableContentKey content))
            {
                return;
            }

            Record(
                StatisticKeys.GetItemUseKey(content),
                new StatisticMetrics(events: 1L));
        }

        /// <summary>
        /// 为垃圾槽中已被覆盖、无法再取回的物品记录一次丢弃及其堆叠数量。
        /// </summary>
        internal void RecordItemDiscard(Item item)
        {
            if (!IsOwnedByLocalClient
                || item == null
                || item.IsAir
                || item.stack <= 0
                || !StableContentKey.TryCreateForItem(
                    item.type,
                    out StableContentKey content))
            {
                return;
            }

            Record(
                StatisticKeys.GetItemDiscardKey(content),
                new StatisticMetrics(events: 1L, units: item.stack));
        }

        /// <summary>
        /// 记录一次物品动画周期，并将持续引导物品在按键释放前归并为同一次使用。
        /// </summary>
        internal void RecordItemUseAnimation(Item item)
        {
            if (!IsOwnedByLocalClient || item == null || item.IsAir)
            {
                return;
            }

            // ItemCheck_StartActualUse 会先执行 StartChanneling 再触发 UseAnimation，
            // 因而这里能可靠识别标准引导入口。模组若在同一引导会话内手动再次调用
            // ApplyItemAnimation，也会被同一物品类型和左右键模式组成的会话键拦截。
            // Player.channel 也覆盖模组通过 Player.StartChanneling() 建立、但未静态设置
            // Item.channel 的自定义维持型用法。
            bool isChanneledUse = Player.channel;
            int altFunctionUse = Player.altFunctionUse;
            if (isChanneledUse)
            {
                if (hasActiveChanneledItemUse
                    && activeChanneledItemType == item.type
                    && activeChanneledAltFunctionUse == altFunctionUse)
                {
                    return;
                }

                hasActiveChanneledItemUse = true;
                activeChanneledItemType = item.type;
                activeChanneledAltFunctionUse = altFunctionUse;
            }
            else
            {
                // 非引导的多段攻击在同一动画内不会重复进入 UseAnimation；后续新动画
                // 代表新的实际使用周期，必须继续逐周期计数，不能按按键状态合并。
                ResetItemUseTracking();
            }

            RecordItemUse(item);
        }

        public override void PreUpdateMovement()
        {
            if (!IsOwnedByLocalClient)
            {
                hasPreMovementMode = false;
                preMovementContent = null;
                return;
            }

            // 原版 GrappleMovement 已在此回调之前完成，钩爪数组仍代表本帧实际采用的
            // 牵引状态；到 PostUpdate 时部分钩爪会清理该状态，因此必须在位移前快照。
            preMovementMode = ClassifyCurrentMovementMode(out preMovementContent);
            hasPreMovementMode = true;
        }

        public override void OnRespawn()
        {
            hasRecordedCurrentDeath = false;
            ResetLifeLossTracking();
            ResetMovementTracking();
            ResetItemUseTracking();
            pendingPickupObservations.Clear();
        }

        /// <summary>
        /// 在持续引导结束后释放去重会话，使下一次按下能够正常计数。
        /// </summary>
        private void UpdateItemUseTracking()
        {
            if (!Player.channel)
            {
                ResetItemUseTracking();
            }
        }

        /// <summary>
        /// 清除仅在一次持续引导期间有效的物品使用去重状态。
        /// </summary>
        private void ResetItemUseTracking()
        {
            hasActiveChanneledItemUse = false;
            activeChanneledItemType = 0;
            activeChanneledAltFunctionUse = 0;
        }

        /// <summary>
        /// 采样本帧结束位置并累计有效移动距离；传送和跨生命周期位置跳变只重建基准。
        /// </summary>
        private void TrackMovementDistance()
        {
            if (Main.gameMenu
                || Main.gamePaused
                || !Player.active
                || Player.dead
                || Player.ghost
                || Player.teleportTime > 0)
            {
                // 暂停采样时丢弃位置基准，恢复后的第一帧不能连接到暂停前的位置。
                hasMovementSnapshot = false;
                hasPreMovementMode = false;
                return;
            }

            Vector2 currentPosition = Player.position;
            MovementModeKind currentMovementMode = ClassifyCurrentMovementMode(
                out StableContentKey? currentMovementContent);
            MovementModeKind movementModeBeforePositionUpdate = hasPreMovementMode
                ? preMovementMode
                : previousMovementMode;
            StableContentKey? movementContentBeforePositionUpdate = hasPreMovementMode
                ? preMovementContent
                : null;
            hasPreMovementMode = false;
            preMovementContent = null;
            if (!hasMovementSnapshot)
            {
                previousMovementPosition = currentPosition;
                previousMovementMode = currentMovementMode;
                hasMovementSnapshot = true;
                return;
            }

            float distance = Vector2.Distance(previousMovementPosition, currentPosition);
            Vector2 displacement = currentPosition - previousMovementPosition;
            MovementModeKind movementMode = ClassifyMovementSegment(
                movementModeBeforePositionUpdate,
                currentMovementMode,
                displacement.Y);
            StableContentKey? movementContent = movementModeBeforePositionUpdate == movementMode
                ? movementContentBeforePositionUpdate
                : currentMovementMode == movementMode
                    ? currentMovementContent
                    : null;
            previousMovementPosition = currentPosition;
            previousMovementMode = currentMovementMode;

            // NaN、Infinity 和异常跨度均不能污染长期存档；下一帧仍从当前位置继续。
            if (!float.IsFinite(distance)
                || distance <= 0f
                || distance > MaximumTrackedMovementPerTick)
            {
                return;
            }

            // 逐帧保留亚像素精度，只把完整像素提交到 long 指标，避免对角移动的累计舍入偏差。
            pendingMovementPixels += distance;
            long completedPixels = pendingMovementPixels >= long.MaxValue
                ? long.MaxValue
                : (long)Math.Floor(pendingMovementPixels);
            if (completedPixels <= 0L)
            {
                return;
            }

            Record(
                StatisticKeys.TotalMovementDistance,
                new StatisticMetrics(amount: completedPixels));
            // 可解析的特殊移动只写物品明细；基础类别或无法解析的特殊来源写无内容键。
            // UI 由两者共同计算分类合计，避免同时保存合计与明细形成两份真值。
            StatisticKey movementKey = IsItemDetailedMovementMode(movementMode)
                && movementContent.HasValue
                    ? StatisticKeys.GetMovementModeDistanceKey(
                        movementMode,
                        movementContent.Value)
                    : StatisticKeys.GetMovementModeDistanceKey(movementMode);
            Record(movementKey, new StatisticMetrics(amount: completedPixels));
            pendingMovementPixels -= completedPixels;
        }

        /// <summary>
        /// 按当前速度和主动飞行状态生成帧末移动状态，供相邻位置之间的位移归类。
        /// </summary>
        private MovementModeKind ClassifyCurrentMovementMode(
            out StableContentKey? content)
        {
            content = null;
            // 特殊移动方式严格遵循设计优先级。矿车是 Mount 的子集，必须先判断。
            if (Player.mount.Active)
            {
                MovementModeKind mountedMode = Player.mount.Cart
                    ? MovementModeKind.Minecart
                    : MovementModeKind.OtherMount;
                TryResolveMovementItem(mountedMode, out content);
                return mountedMode;
            }

            if (Player.pulley)
            {
                TryResolveMovementItem(MovementModeKind.RopeOrPulley, out content);
                return MovementModeKind.RopeOrPulley;
            }

            if (Player.grapCount > 0
                || (Player.grappling.Length > 0 && Player.grappling[0] >= 0))
            {
                TryResolveMovementItem(MovementModeKind.GrapplingHook, out content);
                return MovementModeKind.GrapplingHook;
            }

            // wingTime 在落地时也会保持充满，因此必须同时要求离地、按住跳跃且有翼逻辑。
            // jump 归零后原版才进入翼的飞行阶段，避免把普通起跳误记为飞行。
            bool isAirborne = Math.Abs(Player.velocity.Y) > 0.01f;
            bool isUsingWings = isAirborne
                && Player.controlJump
                && Player.jump == 0
                && Player.wingsLogic > 0
                && Player.wingTime > 0f;
            // rocketTime 是落地后补满的剩余燃料，不能单独证明正在飞行；rocketDelay
            // 只在火箭靴实际工作期间保持为正，能覆盖松键后的短暂推进阶段。
            bool isUsingRocketBoots = isAirborne
                && Player.rocketDelay > 0;
            if (isUsingWings || isUsingRocketBoots)
            {
                TryResolveMovementItem(MovementModeKind.Flying, out content);
                return MovementModeKind.Flying;
            }

            if (!isAirborne)
            {
                // 有水平速度的接地移动视为步行；无自身速度却发生位置变化的情况
                // 交由“其他或未分类”兜底，例如平台或第三方模组直接移动玩家。
                return Math.Abs(Player.velocity.X) > 0.01f
                    ? MovementModeKind.Walking
                    : MovementModeKind.Other;
            }

            return Player.velocity.Y * Player.gravDir > 0.01f
                ? MovementModeKind.Falling
                : ResolveJumpingMovement(out content);
        }

        /// <summary>
        /// 将上升位移归为跳跃，并在原版额外跳跃生效时解析其来源饰品。
        /// </summary>
        private MovementModeKind ResolveJumpingMovement(out StableContentKey? content)
        {
            TryResolveMovementItem(MovementModeKind.Jumping, out content);
            return MovementModeKind.Jumping;
        }

        /// <summary>
        /// 尝试解析特殊移动状态实际使用的物品，以稳定内容键保存可展开明细。
        /// </summary>
        private bool TryResolveMovementItem(
            MovementModeKind mode,
            out StableContentKey? content)
        {
            int itemType = mode switch
            {
                MovementModeKind.Minecart => GetMiscEquipItemType(Player.miscSlotCart),
                MovementModeKind.OtherMount => GetMiscEquipItemType(Player.miscSlotMount),
                MovementModeKind.GrapplingHook => GetQuickGrappleItemType(),
                MovementModeKind.Flying => FindFlightItemType(),
                MovementModeKind.RopeOrPulley => FindRopeItemType(),
                MovementModeKind.Jumping => FindActiveExtraJumpItemType(),
                _ => 0
            };

            // 未装备专用矿车时原版仍允许使用默认木矿车，以对应物品作为稳定明细。
            if (mode == MovementModeKind.Minecart && itemType == 0)
            {
                itemType = ItemID.Minecart;
            }

            if (itemType > ItemID.None
                && StableContentKey.TryCreateForItem(itemType, out StableContentKey key))
            {
                content = key;
                return true;
            }

            content = null;
            return false;
        }

        /// <summary>
        /// 获取指定原版杂项装备槽中的物品类型。
        /// </summary>
        private int GetMiscEquipItemType(int slot)
        {
            return slot >= 0
                && slot < Player.miscEquips.Length
                && !Player.miscEquips[slot].IsAir
                    ? Player.miscEquips[slot].type
                    : 0;
        }

        /// <summary>
        /// 按原版快速钩爪规则取得来源物品：优先装备槽，否则取背包前 58 格中的第一个钩爪。
        /// </summary>
        private int GetQuickGrappleItemType()
        {
            Item item = Player.QuickGrapple_GetItemToUse();
            return item != null && !item.IsAir ? item.type : 0;
        }

        /// <summary>
        /// 从功能装备槽中查找当前提供翅膀或火箭靴飞行效果的物品。
        /// </summary>
        private int FindFlightItemType()
        {
            // armor 的前半保存功能装备，后半为社交栏；不能让外观翅膀抢占统计来源。
            int functionalSlotCount = Player.armor.Length / 2;
            if (Player.wingsLogic > 0)
            {
                for (int index = 0; index < functionalSlotCount; index++)
                {
                    Item item = Player.armor[index];
                    if (!item.IsAir && item.wingSlot == Player.wingsLogic)
                    {
                        return item.type;
                    }
                }
            }

            if (Player.rocketDelay > 0)
            {
                for (int index = 0; index < functionalSlotCount; index++)
                {
                    Item item = Player.armor[index];
                    if (!item.IsAir && item.shoeSlot > 0 && item.shoeSlot == Player.shoe)
                    {
                        return item.type;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// 查找当前正在提供额外跳跃的原版功能饰品；普通跳跃与无法可靠映射的模组跳跃不猜测来源。
        /// </summary>
        private int FindActiveExtraJumpItemType()
        {
            ExtraJump activeJump = null;
            foreach (ExtraJump jump in ExtraJumpLoader.OrderedJumps)
            {
                if (Player.GetJumpState(jump).Active)
                {
                    activeJump = jump;
                    break;
                }
            }

            if (activeJump == null)
            {
                return 0;
            }

            // armor 的前半保存功能装备，后半为社交栏；同类跳跃存在重复来源时按
            // 原版装备栏顺序选择第一件，保证记录结果稳定且不受外观栏影响。
            int functionalSlotCount = Player.armor.Length / 2;
            for (int index = 0; index < functionalSlotCount; index++)
            {
                Item item = Player.armor[index];
                if (!item.IsAir && ProvidesVanillaExtraJump(item.type, activeJump))
                {
                    return item.type;
                }
            }

            return 0;
        }

        /// <summary>
        /// 判断一个原版饰品是否提供指定的原版额外跳跃，包括其气球、马掌与气球束合成形态。
        /// </summary>
        private static bool ProvidesVanillaExtraJump(int itemType, ExtraJump jump)
        {
            if (jump == ExtraJump.CloudInABottle)
            {
                return itemType == ItemID.CloudinaBottle
                    || itemType == ItemID.CloudinaBalloon
                    || itemType == ItemID.BlueHorseshoeBalloon
                    || itemType == ItemID.BundleofBalloons
                    || itemType == ItemID.HorseshoeBundle;
            }

            if (jump == ExtraJump.BlizzardInABottle)
            {
                return itemType == ItemID.BlizzardinaBottle
                    || itemType == ItemID.BlizzardinaBalloon
                    || itemType == ItemID.WhiteHorseshoeBalloon
                    || itemType == ItemID.BundleofBalloons
                    || itemType == ItemID.HorseshoeBundle;
            }

            if (jump == ExtraJump.SandstormInABottle)
            {
                return itemType == ItemID.SandstorminaBottle
                    || itemType == ItemID.SandstorminaBalloon
                    || itemType == ItemID.YellowHorseshoeBalloon
                    || itemType == ItemID.BundleofBalloons
                    || itemType == ItemID.HorseshoeBundle;
            }

            if (jump == ExtraJump.FartInAJar)
            {
                return itemType == ItemID.FartinaJar
                    || itemType == ItemID.FartInABalloon
                    || itemType == ItemID.BalloonHorseshoeFart;
            }

            if (jump == ExtraJump.TsunamiInABottle)
            {
                return itemType == ItemID.TsunamiInABottle
                    || itemType == ItemID.SharkronBalloon
                    || itemType == ItemID.BalloonHorseshoeSharkron;
            }

            if (jump == ExtraJump.Flipper)
            {
                return itemType == ItemID.Flipper
                    || itemType == ItemID.DivingGear
                    || itemType == ItemID.JellyfishDivingGear
                    || itemType == ItemID.ArcticDivingGear
                    || itemType == ItemID.FrogFlipper
                    || itemType == ItemID.FrogGear;
            }

            return false;
        }

        /// <summary>
        /// 查找角色碰撞箱附近正在攀附的绳索物块所对应的掉落物品。
        /// </summary>
        private int FindRopeItemType()
        {
            int leftTile = Math.Max(1, (int)(Player.Left.X / 16f) - 1);
            int rightTile = Math.Min(Main.maxTilesX - 2, (int)(Player.Right.X / 16f) + 1);
            int topTile = Math.Max(1, (int)(Player.Top.Y / 16f) - 1);
            int bottomTile = Math.Min(Main.maxTilesY - 2, (int)(Player.Bottom.Y / 16f) + 1);

            for (int tileX = leftTile; tileX <= rightTile; tileX++)
            {
                for (int tileY = topTile; tileY <= bottomTile; tileY++)
                {
                    Tile tile = Framing.GetTileSafely(tileX, tileY);
                    int tileType = tile.TileType;
                    if (!tile.HasTile
                        || tileType < 0
                        || tileType >= Main.tileRope.Length
                        || !Main.tileRope[tileType])
                    {
                        continue;
                    }

                    int style = Math.Max(tile.TileFrameX / 18, 0);
                    int itemType = TileLoader.GetItemDropFromTypeAndStyle(tileType, style);
                    if (itemType > ItemID.None)
                    {
                        return itemType;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// 根据位移两端状态与重力方向，为这一段实际路径选择唯一基础移动类别。
        /// </summary>
        private MovementModeKind ClassifyMovementSegment(
            MovementModeKind previousMode,
            MovementModeKind currentMode,
            float verticalDisplacement)
        {
            // 位移两端可能正好发生状态切换；先按特殊移动优先级选择任一端的状态，
            // 避免上车、下车或松开钩爪的边界帧被较低类别截走。
            foreach (MovementModeKind specialMode in SpecialMovementModePriority)
            {
                if (previousMode == specialMode || currentMode == specialMode)
                {
                    return specialMode;
                }
            }

            bool isAirborne = IsBasicAirborneMode(previousMode)
                || IsBasicAirborneMode(currentMode);
            if (!isAirborne)
            {
                // 坡面行走会改变 position.Y，但两端均接地时仍应归入步行。
                return previousMode == MovementModeKind.Walking
                        || currentMode == MovementModeKind.Walking
                    ? MovementModeKind.Walking
                    : MovementModeKind.Other;
            }

            float gravityAdjustedDisplacement = verticalDisplacement * Player.gravDir;
            if (gravityAdjustedDisplacement > 0.01f)
            {
                return MovementModeKind.Falling;
            }

            if (gravityAdjustedDisplacement < -0.01f)
            {
                return MovementModeKind.Jumping;
            }

            // 跳跃顶点可能只有水平位移，此时沿用两端已有的空中方向而不退回步行。
            return previousMode == MovementModeKind.Falling
                    || currentMode == MovementModeKind.Falling
                ? MovementModeKind.Falling
                : MovementModeKind.Jumping;
        }

        /// <summary>
        /// 判断一个基础状态是否表示未被特殊方式接管的空中移动。
        /// </summary>
        private static bool IsBasicAirborneMode(MovementModeKind mode)
        {
            return mode == MovementModeKind.Jumping
                || mode == MovementModeKind.Falling;
        }

        /// <summary>
        /// 判断移动类别是否要求按实际来源物品保存明细。
        /// </summary>
        private static bool IsItemDetailedMovementMode(MovementModeKind mode)
        {
            return mode == MovementModeKind.Minecart
                || mode == MovementModeKind.OtherMount
                || mode == MovementModeKind.RopeOrPulley
                || mode == MovementModeKind.GrapplingHook
                || mode == MovementModeKind.Flying
                || mode == MovementModeKind.Jumping;
        }

        /// <summary>
        /// 清除仅在当前连续活动阶段有效的位置基准与亚像素余量。
        /// </summary>
        private void ResetMovementTracking()
        {
            hasMovementSnapshot = false;
            previousMovementPosition = Vector2.Zero;
            previousMovementMode = default;
            hasPreMovementMode = false;
            preMovementMode = default;
            preMovementContent = null;
            pendingMovementPixels = 0d;
        }

        /// <summary>
        /// 在原版入包逻辑完成后结算本帧拾取，只为实际离开世界物品堆叠的数量记一次事件。
        /// </summary>
        private void ReconcileItemPickups()
        {
            foreach (PendingPickupObservation observation in pendingPickupObservations)
            {
                int remainingStack = ResolveRemainingWorldStack(observation);
                long pickedUnits = Math.Max(
                    (long)observation.OriginalStack - remainingStack,
                    0L);
                if (pickedUnits <= 0L)
                {
                    continue;
                }

                Record(
                    StatisticKeys.GetItemPickupKey(observation.Content),
                    new StatisticMetrics(events: 1L, units: pickedUnits));
            }

            pendingPickupObservations.Clear();
        }

        /// <summary>
        /// 取得回调对应世界物品在入包后的剩余数量；槽位被清空或替换表示原堆叠已全部处理。
        /// </summary>
        private static int ResolveRemainingWorldStack(PendingPickupObservation observation)
        {
            Item currentWorldItem = observation.WorldItemIndex >= 0
                && observation.WorldItemIndex < Main.item.Length
                    ? Main.item[observation.WorldItemIndex]
                    : null;

            if (ReferenceEquals(currentWorldItem, observation.ItemBeforePickup))
            {
                return Math.Clamp(
                    observation.ItemBeforePickup.stack,
                    0,
                    observation.OriginalStack);
            }

            // 某些入包路径会以剩余物品替换世界槽位而不是原地修改实例。
            if (currentWorldItem?.active == true
                && currentWorldItem.type == observation.ItemType)
            {
                return Math.Clamp(currentWorldItem.stack, 0, observation.OriginalStack);
            }

            return 0;
        }

        /// <summary>
        /// 将一次统计增量提交到角色生涯及当前世界切片。
        /// </summary>
        internal void Record(StatisticKey key, StatisticMetrics delta)
        {
            EnsureCurrentWorldSlice();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Data.Add(currentWorldId, key, delta, now);
            currentWorldSlice = Data.GetOrCreateWorldSlice(currentWorldId, now);
        }

        /// <summary>
        /// 确保缓存的世界统计切片与当前世界唯一标识一致。
        /// </summary>
        private void EnsureCurrentWorldSlice()
        {
            Guid activeWorldId = Main.ActiveWorldFileData?.UniqueId ?? Guid.Empty;
            if (currentWorldSlice != null && currentWorldId == activeWorldId)
            {
                return;
            }

            currentWorldId = activeWorldId;
            currentWorldSlice = Data.GetOrCreateWorldSlice(activeWorldId, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// 在首次参与生命差额结算前建立基准快照。
        /// </summary>
        private void EnsureLifeSnapshot()
        {
            if (hasLifeSnapshot)
            {
                return;
            }

            previousLifeSnapshot = Math.Max(Player.statLife, 0);
            pendingDirectLifeLost = 0L;
            hasLifeSnapshot = true;
            ClearPendingDeathSources();
        }

        /// <summary>
        /// 以玩家当前生命值重置直接伤害和致死来源的临时状态。
        /// </summary>
        private void ResetLifeLossTracking()
        {
            previousLifeSnapshot = Math.Max(Player.statLife, 0);
            pendingDirectLifeLost = 0L;
            hasLifeSnapshot = true;
            ClearPendingDeathSources();
        }

        /// <summary>
        /// 对比相邻帧生命快照，补记未经过普通受伤回调的生命损失。
        /// </summary>
        private void ReconcileLifeLoss()
        {
            EnsureLifeSnapshot();

            int currentLife = Math.Max(Player.statLife, 0);
            long observedLifeLost = Math.Max((long)previousLifeSnapshot - currentLife, 0L);
            long unreportedLifeLost = Math.Max(
                observedLifeLost - pendingDirectLifeLost,
                0L);

            if (unreportedLifeLost > 0L)
            {
                if (IsDrowningLifeLoss())
                {
                    // 原版溺水直接修改 statLife，不触发 OnHurt；在快照差额中优先回填到
                    // C2 的互斥直接伤害类别，避免落入 C3 的未知生命流失。
                    Record(
                        StatisticKeys.DirectLifeLost,
                        new StatisticMetrics(amount: unreportedLifeLost));
                    Record(
                        StatisticKeys.GetDirectDamageSourceKey(
                            DirectDamageSourceKind.Drowning),
                        new StatisticMetrics(amount: unreportedLifeLost));

                    if (currentLife <= 0)
                    {
                        hasPendingDirectDeathSource = true;
                        pendingDirectDeathSource = DirectDamageSourceKind.Drowning;
                        pendingDirectDeathNpc = null;
                        pendingContinuousDeathSource = null;
                    }
                }
                else
                {
                    StatisticKey deathSource = RecordContinuousLifeLoss(unreportedLifeLost);
                    if (currentLife <= 0)
                    {
                        pendingContinuousDeathSource = deathSource;
                    }
                }
            }

            // 直接伤害可能与同周期治疗相抵；周期结束后仍须清空，不能挪去抵消下一次流失。
            previousLifeSnapshot = currentLife;
            pendingDirectLifeLost = 0L;
            if (!Player.dead)
            {
                // 兼容其他模组取消死亡或立即救活玩家的情况，避免保留一次未发生的致死来源。
                ClearPendingDeathSources();
            }
        }

        /// <summary>
        /// 判断当前未报告生命损失是否符合原版溺水条件。
        /// </summary>
        private bool IsDrowningLifeLoss()
        {
            // breath 为 0 后才会产生溺水扣血；排除不会使用普通呼吸规则的液体与形态。
            return Player.breath <= 0
                && Player.wet
                && !Player.lavaWet
                && !Player.shimmerWet
                && !Player.gills
                && !Player.merman;
        }

        /// <summary>
        /// 记录一段持续生命损失，并返回同来源的死亡统计键。
        /// </summary>
        private StatisticKey RecordContinuousLifeLoss(long amount)
        {
            ContinuousLifeLossSourceKind source = ContinuousLifeLossClassifier.Classify(
                Player,
                out int singleDebuffType);
            StatisticKey key;
            StatisticKey deathKey;

            if (source == ContinuousLifeLossSourceKind.SingleDebuff
                && StableContentKey.TryCreateForBuff(
                    singleDebuffType,
                    out StableContentKey buffKey))
            {
                key = StatisticKeys.GetContinuousDamageDebuffKey(buffKey);
                deathKey = StatisticKeys.GetContinuousDamageDebuffDeathKey(buffKey);
            }
            else if (source == ContinuousLifeLossSourceKind.MultipleDebuffs)
            {
                key = StatisticKeys.MultipleContinuousDamageEffects;
                deathKey = StatisticKeys.MultipleContinuousDamageDeaths;
            }
            else
            {
                key = StatisticKeys.UnknownContinuousLifeLoss;
                deathKey = StatisticKeys.UnknownContinuousLifeLossDeaths;
            }

            Record(key, new StatisticMetrics(amount: amount));
            return deathKey;
        }

        /// <summary>
        /// 按已捕获来源、死亡原因和未知来源的优先级记录一次死亡归因。
        /// </summary>
        private void RecordDeathSource(PlayerDeathReason reason, bool pvp)
        {
            if (hasPendingDirectDeathSource)
            {
                Record(
                    StatisticKeys.GetDirectDeathSourceKey(pendingDirectDeathSource),
                    new StatisticMetrics(events: 1L));
                if (pendingDirectDeathNpc.HasValue)
                {
                    Record(
                        StatisticKeys.GetNpcDirectDeathKey(pendingDirectDeathNpc.Value),
                        new StatisticMetrics(events: 1L));
                }

                return;
            }

            if (pendingContinuousDeathSource.HasValue)
            {
                Record(
                    pendingContinuousDeathSource.Value,
                    new StatisticMetrics(events: 1L));
                return;
            }

            if (DirectDamageSourceClassifier.TryClassifyDeathReason(
                reason,
                pvp,
                out DirectDamageSourceKind directSource,
                out NPC npcSource))
            {
                Record(
                    StatisticKeys.GetDirectDeathSourceKey(directSource),
                    new StatisticMetrics(events: 1L));
                if (npcSource != null
                    && StableContentKey.TryCreateForNpc(
                        npcSource,
                        out StableContentKey npcKey))
                {
                    Record(
                        StatisticKeys.GetNpcDirectDeathKey(npcKey),
                        new StatisticMetrics(events: 1L));
                }

                return;
            }

            Record(StatisticKeys.UnknownDeathSource, new StatisticMetrics(events: 1L));
        }

        /// <summary>
        /// 清除仅对当前生命结算周期有效的候选致死来源。
        /// </summary>
        private void ClearPendingDeathSources()
        {
            hasPendingDirectDeathSource = false;
            pendingDirectDeathSource = default;
            pendingDirectDeathNpc = null;
            pendingContinuousDeathSource = null;
        }

        /// <summary>
        /// 安全累加生命损失，避免极端模组数值造成整数溢出。
        /// </summary>
        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private bool IsOwnedByLocalClient =>
            Main.netMode != NetmodeID.Server && Player.whoAmI == Main.myPlayer;

        /// <summary>
        /// 保存一次 OnPickup 前的世界物品快照，供同帧结束时确认实际转移数量。
        /// </summary>
        private readonly struct PendingPickupObservation
        {
            /// <summary>
            /// 创建一次待结算的世界物品拾取观察。
            /// </summary>
            internal PendingPickupObservation(
                Item itemBeforePickup,
                int worldItemIndex,
                int itemType,
                int originalStack,
                StableContentKey content)
            {
                ItemBeforePickup = itemBeforePickup;
                WorldItemIndex = worldItemIndex;
                ItemType = itemType;
                OriginalStack = originalStack;
                Content = content;
            }

            internal Item ItemBeforePickup { get; }

            internal int WorldItemIndex { get; }

            internal int ItemType { get; }

            internal int OriginalStack { get; }

            internal StableContentKey Content { get; }
        }
    }
}
