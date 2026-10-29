using System;
using System.Collections.Generic;
using ComprehensiveStatistics.Common.Players;
using Terraria;
using Terraria.ID;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 一个最小同步步骤的意图；显式清理与后续放置必须使用不同步骤，不能只计算任务净差。
    /// </summary>
    internal enum TileChangeIntent { Place, Remove, Replace }

    /// <summary>
    /// 隔离物块和墙的观察目标，防止为铺墙而清理物块时丢失独立拆除结果。
    /// </summary>
    internal enum TileChangeLayer { Tile, Wall, Wiring }

    /// <summary>
    /// 关联一个本地玩家操作或已验证的服务器任务，收集目标值快照并由最外层统一结算嵌套调用。
    /// 此类型不自行给第三方任务归属；适配器必须在每次实际执行步骤恢复明确的玩家身份。
    /// </summary>
    internal sealed class TileChangeScope : IDisposable
    {
        [ThreadStatic]
        private static TileChangeScope current;
        private static long worldSession;
        private readonly TileChangeScope previous;
        private readonly TileChangeScope owner;
        private readonly Player player;
        private readonly long session;
        private readonly Dictionary<(TileChangeLayer Layer, int X, int Y), Target> targets;
        private bool disposed;
        private readonly bool suppressed;
        private readonly bool directTargetsOnly;
        private readonly Action<TileChangeResult> verifiedSink;
        private readonly ITileChangeCandidateObserver candidateObserver;
        internal static long WorldSession => worldSession;

        /// <summary>
        /// 建立可嵌套作用域；同一角色的内部调用由外层收集，其他角色或无效入口暂时屏蔽归属。
        /// </summary>
        private TileChangeScope(Player player, bool suppress = false, bool independent = false, bool directTargetsOnly = false,
            Action<TileChangeResult> verifiedSink = null, ITileChangeCandidateObserver candidateObserver = null)
        {
            previous = current;
            suppressed = suppress || previous?.suppressed == true;
            this.directTargetsOnly = directTargetsOnly;
            this.verifiedSink = verifiedSink ?? (!independent && previous?.player == player ? previous?.owner.verifiedSink : null);
            this.candidateObserver = candidateObserver ?? (previous?.player == player ? previous?.owner.candidateObserver : null);
            this.player = !suppressed && (this.verifiedSink != null ? IsServerPlayer(player) : IsLocalPlayer(player)) ? player : null;
            session = worldSession;
            owner = (!independent || this.candidateObserver != null) && this.player != null && previous?.player == this.player
                && previous.session == session ? previous.owner : this;
            if (owner == this)
                targets = new Dictionary<(TileChangeLayer, int, int), Target>();
            current = this;
        }

        /// <summary>
        /// 为一次同步调用建立作用域；返回对象必须由 using 或 finally 释放。
        /// </summary>
        internal static TileChangeScope Begin(Player player, bool directTargetsOnly = false)
            => new TileChangeScope(player, directTargetsOnly: directTargetsOnly);

        /// <summary>开启独立清理或替换步骤，防止同格清理后重建被外层净差抵消。</summary>
        internal static TileChangeScope BeginStep(Player player, bool directTargetsOnly = false)
            => new TileChangeScope(player, independent: true, directTargetsOnly: directTargetsOnly);

        /// <summary>仅供已绑定真实服务器任务的适配器使用；服务器结果交给回传器，不访问角色生涯。</summary>
        internal static TileChangeScope BeginVerified(Player player, Action<TileChangeResult> sink)
            => sink == null ? Suppress() : new TileChangeScope(player, directTargetsOnly: true, verifiedSink: sink);

        /// <summary>为已握手的客户端施工收集候选；同一窗口中的子步骤合并为一套直接目标。</summary>
        internal static TileChangeScope BeginCandidate(Player player, ITileChangeCandidateObserver observer)
            => observer == null ? Suppress() : new TileChangeScope(player, directTargetsOnly: true, candidateObserver: observer);

        /// <summary>阻止未确认的多人施工通过任何嵌套原版入口提前记账。</summary>
        internal static TileChangeScope Suppress() => new TileChangeScope(null, suppress: true);

        /// <summary>
        /// 注册直接目标的首次快照；内部 Place/Replace/Pick 重入不得覆盖外层语义及修改前状态。
        /// </summary>
        internal static void Observe(int x, int y, TileChangeLayer layer, TileChangeIntent intent, bool direct = false)
        {
            TileChangeScope scope = current?.owner;
            if (current?.player == null || scope == null || scope.disposed
                || scope.session != worldSession || !WorldGen.InWorld(x, y))
                return;
            var key = (layer, x, y);
            // 兼容步骤只允许受信辅助入口声明目标；原版内部递归放置/更新不能扩大施工归属范围。
            if (scope.directTargetsOnly && !direct && !scope.targets.ContainsKey(key))
                return;
            if (!scope.targets.ContainsKey(key))
            {
                scope.candidateObserver?.Observe();
                scope.targets.Add(key, new Target(TileContentSnapshot.Capture(x, y), intent));
            }
            else if (scope.candidateObserver != null && scope.targets[key].Intent != intent)
                scope.targets[key].Intent = TileChangeIntent.Replace;
        }

        /// <summary>
        /// 清除世界生命周期上下文；旧步骤即使之后释放，也不能把结果写入新世界或新角色。
        /// </summary>
        internal static void Reset()
        {
            worldSession++;
            current = null;
        }

        /// <summary>在底层破坏返回后记录旧内容确实消失的证据，使同类型拆后重放不会被最终净差抵消。</summary>
        internal static void NoteRemoval(int x, int y, TileChangeLayer layer)
        {
            TileChangeScope scope = current?.owner;
            if (current?.player == null || scope?.session != worldSession || scope.disposed
                || !scope.targets.TryGetValue((layer, x, y), out Target target))
                return;
            TileContentSnapshot after = TileContentSnapshot.Capture(x, y);
            if (!after.IsValid) return;
            bool wasRemoved = target.Removed;
            if (layer == TileChangeLayer.Wall)
                target.Removed |= target.Before.WallType != WallID.None && after.WallType == WallID.None;
            else if (layer == TileChangeLayer.Tile)
                target.Removed |= target.Before.HasTile && target.Before.HasKnownIdentity
                    && (target.Before.IsObject ? target.Before.IsFullyRemoved() : !after.HasTile);
            if (!wasRemoved && target.Removed) scope.candidateObserver?.Removed(x, y, layer, target.Intent, target.Before);
        }

        /// <summary>
        /// 判断本地玩家身份；加载模组时不访问 ModPlayer，实际提交时才读取其统计实例。
        /// </summary>
        private static bool IsLocalPlayer(Player value)
        {
            return !Main.dedServ && !Main.gameMenu && value?.active == true
                && Main.myPlayer >= 0 && Main.myPlayer < Main.player.Length
                && value.whoAmI == Main.myPlayer
                && Main.player[Main.myPlayer] == value;
        }

        /// <summary>验证服务器上的实际玩家实例，不读取 Main.LocalPlayer。</summary>
        private static bool IsServerPlayer(Player value) => Main.netMode == NetmodeID.Server && !Main.gameMenu && value?.active == true
            && value.whoAmI >= 0 && value.whoAmI < Main.player.Length && Main.player[value.whoAmI] == value;

        /// <summary>
        /// 先恢复上一层再提交，防止提交过程意外继承世界操作归属；重复释放不会再次入账。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (current == this)
                current = session == worldSession ? previous : null;
            if (owner != this || session != worldSession
                || !(verifiedSink != null ? IsServerPlayer(player) : IsLocalPlayer(player)) || targets.Count == 0)
                return;

            if (candidateObserver != null)
            {
                var candidates = new List<TileChangeCandidate>(targets.Count);
                foreach (var pair in targets)
                    candidates.Add(new TileChangeCandidate(pair.Key.X, pair.Key.Y, pair.Key.Layer, pair.Value.Intent,
                        pair.Value.Before, TileContentSnapshot.Capture(pair.Key.X, pair.Key.Y)));
                candidateObserver.Complete(candidates);
                return;
            }

            var seen = new HashSet<(TileChangeLayer Layer, bool Placed, int X, int Y, int Type, int Style)>();
            StatisticsPlayer statistics = verifiedSink == null ? player.GetModPlayer<StatisticsPlayer>() : null;
            foreach (var pair in targets)
            {
                var key = pair.Key;
                Target target = pair.Value;
                TileContentSnapshot before = target.Before;
                TileContentSnapshot after = TileContentSnapshot.Capture(key.X, key.Y);
                if (!before.IsValid || !after.IsValid)
                    continue;
                if (key.Layer == TileChangeLayer.Wiring)
                {
                    for (int component = 0; component < 5; component++)
                    {
                        int mask = 1 << component;
                        bool wasPresent = (before.Wires & mask) != 0;
                        bool isPresent = (after.Wires & mask) != 0;
                        if (wasPresent != isPresent)
                            if (verifiedSink != null)
                                verifiedSink(new TileChangeResult(isPresent ? TileChangeKind.WiringPlaced : TileChangeKind.WiringRemoved, component));
                            else statistics.RecordWiringComponentChanges((WiringComponentKind)component, isPresent, 1L);
                    }
                    continue;
                }
                if (key.Layer == TileChangeLayer.Wall)
                {
                    bool changed = before.WallType != after.WallType || target.Removed;
                    bool placed = target.Intent != TileChangeIntent.Remove && after.WallType != WallID.None && changed;
                    bool removed = before.WallType != WallID.None && changed;
                    if (removed)
                        Emit(statistics, new TileChangeResult(TileChangeKind.WallRemoved, before.WallType));
                    if (placed)
                        Emit(statistics, new TileChangeResult(TileChangeKind.WallPlaced, after.WallType));
                    continue;
                }

                if (before.SameContent(after) && !target.Removed)
                    continue;
                bool tilePlaced = target.Intent != TileChangeIntent.Remove && after.IsFullyPresent();
                if (before.HasTile && before.HasKnownIdentity
                    && (!before.IsObject || target.Removed || before.IsFullyRemoved()))
                    Submit(statistics, before, key.X, key.Y, false, seen);
                if (tilePlaced)
                    Submit(statistics, after, key.X, key.Y, true, seen);
            }
        }

        /// <summary>
        /// 以完整对象原点去重，普通地形按坐标独立提交；沿用已有统计键和数值字段。
        /// </summary>
        private void Submit(StatisticsPlayer statistics, TileContentSnapshot value,
            int x, int y, bool placed,
            HashSet<(TileChangeLayer Layer, bool Placed, int X, int Y, int Type, int Style)> seen)
        {
            if (!seen.Add((TileChangeLayer.Tile, placed,
                value.IsObject ? value.Origin.X : x, value.IsObject ? value.Origin.Y : y,
                value.TileType, value.Style)))
                return;
            Emit(statistics, new TileChangeResult(value.IsObject
                ? (placed ? TileChangeKind.ObjectPlaced : TileChangeKind.ObjectRemoved)
                : (placed ? TileChangeKind.TilePlaced : TileChangeKind.TileRemoved), value.TileType, value.IsObject ? value.Style : 0));
        }

        /// <summary>将同一结算结果发给本地记录器或服务器任务接收器，两者不会同时执行。</summary>
        private void Emit(StatisticsPlayer statistics, TileChangeResult result)
        {
            if (verifiedSink != null) { verifiedSink(result); return; }
            switch (result.Kind)
            {
                case TileChangeKind.TilePlaced: statistics.RecordPlacedTile(result.Type); break;
                case TileChangeKind.TileRemoved: statistics.RecordMinedTile(result.Type); break;
                case TileChangeKind.ObjectPlaced: statistics.RecordPlacedTileObject(result.Type, result.Style); break;
                case TileChangeKind.ObjectRemoved: statistics.RecordRemovedTileObject(result.Type, result.Style); break;
                case TileChangeKind.WallPlaced: statistics.RecordPlacedWall(result.Type); break;
                case TileChangeKind.WallRemoved: statistics.RecordRemovedWall(result.Type); break;
            }
        }

        /// <summary>
        /// 保存一个直接目标在同步步骤开始前的状态及外层声明的意图。
        /// </summary>
        private sealed class Target
        {
            /// <summary>
            /// 绑定修改前值与意图；不引用可变的世界格位。
            /// </summary>
            internal Target(TileContentSnapshot before, TileChangeIntent intent)
            {
                Before = before;
                Intent = intent;
            }

            internal TileContentSnapshot Before { get; }
            internal TileChangeIntent Intent { get; set; }
            internal bool Removed { get; set; }
        }
    }
}
