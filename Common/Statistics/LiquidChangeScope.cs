using System;
using System.Collections.Generic;
using ComprehensiveStatistics.Common.Players;
using Terraria;
using Terraria.ID;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 一个已归属玩家的同步液体步骤；嵌套步骤先结算外层并在返回后重建快照，避免重复提交。
    /// 自然更新使用抑制作用域，其结果不会混入玩家的直接液体变化。
    /// </summary>
    internal sealed class LiquidChangeScope : IDisposable
    {
        [ThreadStatic] private static LiquidChangeScope current;
        private static long worldSession;
        private readonly LiquidChangeScope previous;
        private readonly Player player;
        private readonly long session;
        private readonly bool suppressed;
        private readonly Action<LiquidChange> verifiedSink;
        private readonly List<Target> targets;
        private bool disposed;

        internal static long WorldSession => worldSession;
        internal static bool HasScope => current != null;
        internal static bool IsSuppressed => current?.suppressed == true;

        /// <summary>在最多 3×3 直接目标上保存不可变快照，阻止无归属与抑制上下文继承外层归属。</summary>
        private LiquidChangeScope(Player player, int x, int y, int radius, bool suppress, Action<LiquidChange> verifiedSink = null)
        {
            previous = current;
            previous?.Flush();
            session = worldSession;
            this.player = player;
            this.verifiedSink = verifiedSink;
            suppressed = suppress || previous?.suppressed == true || !IsValidOwner();
            current = this;
            if (suppressed) return;
            targets = new List<Target>(radius == 0 ? 1 : 9);
            for (int i = x - radius; i <= x + radius; i++)
            for (int j = y - radius; j <= y + radius; j++)
            {
                if (!WorldGen.InWorld(i, j)) continue;
                var target = new Target(i, j);
                target.Refresh();
                targets.Add(target);
            }
        }

        /// <summary>建立直接格位或原版 3×3 操作范围；无效根调用不分配作用域，嵌套无效调用必须抑制。</summary>
        internal static LiquidChangeScope Begin(Player player, int x, int y, bool includeNeighbors = false)
        {
            return current == null && !IsLocalSinglePlayer(player) ? null
                : new LiquidChangeScope(player, x, y, includeNeighbors ? 1 : 0, false);
        }

        /// <summary>由已经验证的服务器任务观察一个直接目标，结果只进入定向回传队列。</summary>
        internal static LiquidChangeScope BeginVerified(Player player, int x, int y, Action<LiquidChange> sink)
        {
            return new LiquidChangeScope(player, x, y, 0, sink == null, sink);
        }

        /// <summary>客户端写个人统计，服务器只允许已提供权威结果出口的在线原玩家实例。</summary>
        private bool IsValidOwner()
        {
            return verifiedSink == null ? IsLocalSinglePlayer(player)
                : Main.netMode == NetmodeID.Server && !Main.gameMenu && player?.active == true
                    && player.whoAmI >= 0 && player.whoAmI < Main.player.Length
                    && ReferenceEquals(Main.player[player.whoAmI], player);
        }

        /// <summary>先结算直接变更，再隔离帧修复或液体自然更新，返回时以其结果重建外层基线。</summary>
        internal static LiquidChangeScope SuppressWorldUpdates()
        {
            return new LiquidChangeScope(null, 0, 0, 0, true);
        }

        /// <summary>本出口只允许实际本地单人角色；多人必须使用独立权威出口。</summary>
        internal static bool IsLocalSinglePlayer(Player player)
        {
            return Main.netMode == NetmodeID.SinglePlayer && !Main.dedServ && !Main.gameMenu
                && player?.active == true && Main.myPlayer >= 0 && Main.myPlayer < Main.player.Length
                && player.whoAmI == Main.myPlayer && ReferenceEquals(Main.player[Main.myPlayer], player);
        }

        /// <summary>世界生命周期撤销旧任务和正在执行的旧作用域，旧 finally 不得恢复旧世界上下文。</summary>
        internal static void ResetWorld()
        {
            worldSession++;
            current = null;
        }

        /// <summary>提交每个直接目标的即时差量并推进快照；未知类型或世界失效时不以空格兜底。</summary>
        private void Flush()
        {
            if (suppressed || disposed || session != worldSession || !IsValidOwner()) return;
            foreach (Target target in targets)
            {
                LiquidSnapshot before = target.Value;
                bool validBefore = target.Valid;
                target.Refresh();
                if (!validBefore || !target.Valid) continue;
                LiquidChange change = LiquidChange.Between(before, target.Value);
                if (!change.IsZero)
                {
                    if (verifiedSink != null) verifiedSink(change);
                    else player.GetModPlayer<StatisticsPlayer>().RecordLiquidChange(change);
                }
            }
        }

        /// <summary>内部步骤或自然更新已经独立处理，外层只从返回后的状态继续比较。</summary>
        private void Rebase()
        {
            if (suppressed || disposed || session != worldSession || !IsValidOwner()) return;
            foreach (Target target in targets) target.Refresh();
        }

        /// <summary>异常退出同样保留已完成的直接变化，且只恢复仍属于当前世界的外层作用域。</summary>
        public void Dispose()
        {
            if (disposed) return;
            try { Flush(); }
            finally
            {
                disposed = true;
                if (ReferenceEquals(current, this))
                {
                    current = previous?.session == worldSession ? previous : null;
                    current?.Rebase();
                }
            }
        }

        /// <summary>保存坐标及不可变标量，任何时候都不保留可变 Tile 引用。</summary>
        private sealed class Target
        {
            private readonly int x, y;
            internal LiquidSnapshot Value;
            internal bool Valid;

            /// <summary>固定直接目标坐标。</summary>
            internal Target(int x, int y) { this.x = x; this.y = y; }

            /// <summary>重新读取合法状态；失败时整次比较跳过，不能把未知液体解释成水或空格。</summary>
            internal void Refresh()
            {
                Valid = false;
                if (!WorldGen.InWorld(x, y)) return;
                Tile tile = Framing.GetTileSafely(x, y);
                Valid = LiquidSnapshot.TryCreate(tile.LiquidType, tile.LiquidAmount, out Value);
            }
        }
    }
}
