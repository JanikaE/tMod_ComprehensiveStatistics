using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ID;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>液体能力协商、原版同步操作窗口及服务器实际接受结果的确认。</summary>
    internal sealed partial class ImproveGameMultiplayerSystem
    {
        [ThreadStatic] private static NativeClientStep currentNativeStep;
        [ThreadStatic] private static NativeInbound currentNativeInbound;
        private static volatile NativeClientStep wireNativeStep;
        private static readonly object nativeSendLock = new();
        private static ulong nextNativeOperation;
        private const int MaximumNativeMessages = 32;
        internal static bool HasNativeStep => currentNativeStep != null;
        internal static bool NativeLiquidReady => cursor.Server != Guid.Empty
            && (cursor.Capabilities & liquidCapabilities & ImproveGameCapability.NativeLiquid) != 0;
        internal static bool WandLiquidReady => cursor.Server != Guid.Empty
            && (cursor.Capabilities & Capabilities & ImproveGameCapability.Liquid) != 0;

        /// <summary>原版不依赖 ImproveGame，魔杖须同时具有已安装的采集和接收入口。</summary>
        internal static void PublishLiquid(bool native, bool wand)
        {
            liquidCapabilities = (native ? ImproveGameCapability.NativeLiquid : 0) | (wand ? ImproveGameCapability.Liquid : 0);
        }

        /// <summary>清除未发送完的操作和连接内序号，不跨世界补发。</summary>
        private static void ResetLiquidNetwork()
        { lock (nativeSendLock) { currentNativeStep = null; currentNativeInbound = null; wireNativeStep = null; nextNativeOperation = 0; } }

        /// <summary>损坏的原版液体控制包撤销当前窗口，不能随后用完整提交补记半包结果。</summary>
        private static void RejectNativeFrame(StatisticsPacketType type, int sender)
        {
            if (Main.netMode == NetmodeID.Server && type is StatisticsPacketType.NativeLiquidBegin or StatisticsPacketType.NativeLiquidCommit
                && sessions.TryGetValue(sender, out ServerSession session)) session.NativeWindow = null;
        }

        /// <summary>只对已协商的原玩家建立本地上下文，第一次实际 sendWater 前才发送开始标记。</summary>
        internal static NativeClientStep BeginNativeStep(Player player, int x, int y, int tool)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient || Main.gameMenu || currentNativeStep != null
                || !IsPlayer(player) || player.whoAmI != Main.myPlayer || !NativeLiquidReady
                || nextNativeOperation == ulong.MaxValue || !IsLiquidTool(tool)) return null;
            lock (nativeSendLock)
                return wireNativeStep != null ? null : new NativeClientStep(player, x, y, tool, ++nextNativeOperation);
        }

        /// <summary>将所有实际消息 48 与开始/提交标记串行化，第三方直接 SendData 或后台线程也占据真实包序。</summary>
        internal static IDisposable BeginNativeSend(int x, int y, bool direct)
        {
            if (wireNativeStep == null) return null;
            Monitor.Enter(nativeSendLock);
            try
            {
                wireNativeStep?.BeforeSend(x, y, direct && ReferenceEquals(currentNativeStep, wireNativeStep));
                return new NativeSendLock();
            }
            catch { Monitor.Exit(nativeSendLock); throw; }
        }

        /// <summary>发送原版包后释放发送锁，不能只锁计数而允许包与提交标记乱序。</summary>
        private sealed class NativeSendLock : IDisposable
        {
            /// <summary>退出同一线程上的原版包发送临界区。</summary>
            public void Dispose() => Monitor.Exit(nativeSendLock);
        }

        /// <summary>服务器在原版写入液体之后、帧修复之前捕获实际接受值。</summary>
        internal static void CaptureNativeBeforeFrame(int x, int y)
        { if (currentNativeInbound?.X == x && currentNativeInbound.Y == y) currentNativeInbound.Capture(); }

        /// <summary>原版消息已写完数量和类型的 IL 边界，独立于 SquareTileFrame 是否被上游预编译内联。</summary>
        internal static void CaptureNativeAfterWrite() => currentNativeInbound?.Capture();

        /// <summary>原版十二种工具白名单，独立于 ImproveGame 和本地库存是否被转换。</summary>
        internal static bool IsLiquidTool(int tool) => tool is ItemID.EmptyBucket or ItemID.WaterBucket or ItemID.LavaBucket
            or ItemID.HoneyBucket or ItemID.BottomlessBucket or ItemID.SuperAbsorbantSponge or ItemID.BottomlessLavaBucket
            or ItemID.LavaAbsorbantSponge or ItemID.BottomlessHoneyBucket or ItemID.HoneyAbsorbantSponge
            or ItemID.UltraAbsorbantSponge or ItemID.BottomlessShimmerBucket;

        /// <summary>比较合法液体状态，空格已经由快照归一化。</summary>
        private static bool Same(LiquidSnapshot a, LiquidSnapshot b) => a.Kind == b.Kind && a.Units == b.Units;

        /// <summary>读取一格不可变状态，未知液体和越界不作为空格证据。</summary>
        private static bool ReadLiquid(int x, int y, out LiquidSnapshot value)
        {
            value = default;
            if (!WorldGen.InWorld(x, y)) return false;
            Tile tile = Framing.GetTileSafely(x, y);
            return LiquidSnapshot.TryCreate(tile.LiquidType, tile.LiquidAmount, out value);
        }

        /// <summary>本地同步窗口绑定世界、角色和随机会话，环境同步包不列入直接包序号集合。</summary>
        internal sealed class NativeClientStep : IDisposable
        {
            private readonly Player player;
            private readonly int x, y, tool;
            private readonly ulong operation;
            private readonly Guid client = cursor.Client, server = cursor.Server;
            private readonly long world = LiquidChangeScope.WorldSession;
            private readonly List<ushort> direct = new();
            private int messages;
            private bool started, invalid, disposed;
            private bool IsCurrent => world == LiquidChangeScope.WorldSession && client == cursor.Client && server == cursor.Server
                && NativeLiquidReady && Main.netMode == NetmodeID.MultiplayerClient && !Main.gameMenu
                && IsPlayer(player) && player.whoAmI == Main.myPlayer;

            /// <summary>冻结原版步骤开始时的目标和工具类型，消耗后变成空桶也不改变意图。</summary>
            internal NativeClientStep(Player player, int x, int y, int tool, ulong operation)
            { this.player = player; this.x = x; this.y = y; this.tool = tool; this.operation = operation; currentNativeStep = wireNativeStep = this; }

            /// <summary>第一次直接液体包前发标记，后续所有 sendWater 包计序，环境包只占序号不申报。</summary>
            internal void BeforeSend(int targetX, int targetY, bool directCall)
            {
                if (!IsCurrent || invalid) return;
                bool eligible = directCall && !LiquidChangeScope.IsSuppressed && Math.Abs((long)targetX - x) <= 1
                    && Math.Abs((long)targetY - y) <= 1;
                if (!started)
                {
                    if (!eligible) return;
                    started = true;
                    Send(StatisticsPacketType.NativeLiquidBegin, writer =>
                    { ImproveGameProtocol.WriteSession(writer, client, server); writer.Write(operation);
                        writer.Write(x); writer.Write(y); writer.Write(tool); });
                }
                if (++messages > MaximumNativeMessages || direct.Count >= 9) { invalid = true; return; }
                if (eligible) direct.Add((ushort)messages);
            }

            /// <summary>异常也提交已经发送的直接步骤；无效会话不发旧结果，超预算用空集合撤销。</summary>
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                lock (nativeSendLock)
                {
                    try
                    {
                        if (started && IsCurrent)
                            Send(StatisticsPacketType.NativeLiquidCommit, writer =>
                            { ImproveGameProtocol.WriteSession(writer, client, server); writer.Write(operation);
                                writer.Write((byte)(invalid ? 0 : direct.Count));
                                if (!invalid) foreach (ushort index in direct) writer.Write(index); });
                    }
                    finally
                    {
                        if (currentNativeStep == this) currentNativeStep = null;
                        if (wireNativeStep == this) wireNativeStep = null;
                    }
                }
            }
        }

        /// <summary>完整解析开始/提交后才改变窗口；客户端不发送液体类型、体积或最终统计增量。</summary>
        private static void HandleNativeLiquid(StatisticsPacketType type, BinaryReader reader, ServerSession session)
        {
            if ((session.Capabilities & ImproveGameCapability.NativeLiquid) == 0
                || !ImproveGameProtocol.ReadSession(reader, session.Client, session.Server)) return;
            ulong operation = reader.ReadUInt64();
            if (type == StatisticsPacketType.NativeLiquidBegin)
            {
                int x = reader.ReadInt32(), y = reader.ReadInt32(), tool = reader.ReadInt32();
                if (operation == 0 || operation <= session.LastNativeOperation || !IsLiquidTool(tool)
                    || !WorldGen.InWorld(x, y) || reader.BaseStream.Position != reader.BaseStream.Length
                    || session.NextTask == ulong.MaxValue) return;
                session.LastNativeOperation = operation;
                session.NativeWindow = new ServerNative(operation, new Authority(session, ++session.NextTask,
                    ImproveGameCapability.NativeLiquid), x, y, tool);
                return;
            }
            ServerNative window = session.NativeWindow;
            if (window == null || window.Operation != operation) return;
            try
            {
                int count = reader.ReadByte();
                if (count > 9) return;
                var indices = new HashSet<int>();
                for (int i = 0; i < count; i++) if (!indices.Add(reader.ReadUInt16())) return;
                if (reader.BaseStream.Position != reader.BaseStream.Length || window.Invalid
                    || Main.GameUpdateCount - window.Created > 1800) return;
                var accepted = new List<LiquidChange>();
                var cells = new HashSet<(int, int)>();
                foreach (int index in indices)
                {
                    if (!window.Changes.TryGetValue(index, out NativeEvidence evidence)
                        || !cells.Add((evidence.X, evidence.Y)) || !window.Allows(evidence)) return;
                    accepted.Add(LiquidChange.Between(evidence.Before, evidence.After));
                }
                foreach (LiquidChange change in accepted) window.Authority.RecordLiquid(change);
            }
            finally { session.NativeWindow = null; }
        }

        /// <summary>在真实连接的消息 48 边界保存前值；其他连接修改重叠目标会使待确认窗口失效。</summary>
        private static NativeInbound BeginNativeInbound(MessageBuffer buffer, int start, int length)
        {
            if (Main.netMode != NetmodeID.Server || buffer == null || !inboundPlayer.HasValue
                || start < 0 || length != 7 || start > buffer.readBuffer.Length - length || buffer.readBuffer[start] != 48) return null;
            using var reader = new BinaryReader(new MemoryStream(buffer.readBuffer, start + 1, length - 1, false));
            int x = reader.ReadInt16(), y = reader.ReadInt16(), units = reader.ReadByte(), kind = reader.ReadByte();
            if (!WorldGen.InWorld(x, y) || kind > 3 || !ReadLiquid(x, y, out LiquidSnapshot before)) return null;
            int sender = inboundPlayer.Value;
            foreach (var pair in sessions)
                if (pair.Key != sender && pair.Value.NativeWindow?.Contains(x, y) == true) pair.Value.NativeWindow.Invalid = true;
            if (!sessions.TryGetValue(sender, out ServerSession session) || !session.Ready
                || session.NativeWindow == null) return null;
            ServerNative window = session.NativeWindow;
            if (++window.Messages > MaximumNativeMessages) { window.Invalid = true; return null; }
            LiquidSnapshot.TryCreate(kind, units, out LiquidSnapshot expected);
            return new NativeInbound(window, window.Messages, x, y, before, expected);
        }

        /// <summary>一次原版包的服务器前后证据；帧修复前捕获，原函数异常则撤销整个窗口。</summary>
        private sealed class NativeInbound
        {
            private readonly ServerNative window;
            private readonly int index;
            private readonly LiquidSnapshot before, expected;
            private bool captured;
            internal readonly int X, Y;

            /// <summary>关联真实入站顺序和服务器前值，不信任统计请求里的体积。</summary>
            internal NativeInbound(ServerNative window, int index, int x, int y, LiquidSnapshot before, LiquidSnapshot expected)
            { this.window = window; this.index = index; X = x; Y = y; this.before = before; this.expected = expected; }

            /// <summary>只有原版实际接受了对应状态才留下证据，失败和拒绝不转化为客户端申报结果。</summary>
            internal void Capture()
            {
                if (captured) return;
                captured = true;
                if (!window.Contains(X, Y) || !ReadLiquid(X, Y, out LiquidSnapshot after) || !Same(after, expected)) return;
                if (!window.Baseline.TryGetValue((X, Y), out LiquidSnapshot baseline) || !Same(before, baseline))
                { window.Invalid = true; return; }
                window.Baseline[(X, Y)] = after;
                window.Changes[index] = new NativeEvidence(X, Y, before, after);
            }

            /// <summary>不调用帧修复的原版路径也比较真实结束状态。</summary>
            internal void Complete() => Capture();
            /// <summary>异常处理不能保留半包接受证据。</summary>
            internal void Invalidate() => window.Invalid = true;
        }

        /// <summary>窗口中的一个服务器即时液体状态转移，不保存个人统计。</summary>
        internal readonly record struct NativeEvidence(int X, int Y, LiquidSnapshot Before, LiquidSnapshot After);

        /// <summary>每连接最多一个原版液体窗口，目标最多九格，包序最多三十二。</summary>
        internal sealed class ServerNative
        {
            internal readonly ulong Operation, Created = Main.GameUpdateCount;
            internal readonly Authority Authority;
            internal readonly Dictionary<(int, int), LiquidSnapshot> Baseline = new();
            private readonly Dictionary<int, NativeEvidence> changes = new();
            private readonly int x, y, tool;
            private readonly LiquidSnapshot root;
            internal bool Invalid;
            internal int Messages;
            internal Dictionary<int, NativeEvidence> Changes => changes;

            /// <summary>冻结操作开始时服务器九格状态，以检测自然更新或竞争修改导致的证据失效。</summary>
            internal ServerNative(ulong operation, Authority authority, int x, int y, int tool)
            {
                Operation = operation; Authority = authority; this.x = x; this.y = y; this.tool = tool;
                for (int i = x - 1; i <= x + 1; i++) for (int j = y - 1; j <= y + 1; j++)
                    if (ReadLiquid(i, j, out LiquidSnapshot value)) Baseline[(i, j)] = value;
                if (!Baseline.TryGetValue((x, y), out root)) Invalid = true;
            }

            /// <summary>范围始终为显式中心的 3×3，不按客户端指定任意面积。</summary>
            internal bool Contains(int i, int j) => Math.Abs((long)i - x) <= 1 && Math.Abs((long)j - y) <= 1;

            /// <summary>工具意图限制方向和液体类型；放置只允许中心，移除只允许操作起点同类型。</summary>
            internal bool Allows(NativeEvidence evidence)
            {
                LiquidChange change = LiquidChange.Between(evidence.Before, evidence.After);
                int fill = tool switch { ItemID.WaterBucket or ItemID.BottomlessBucket => 0,
                    ItemID.LavaBucket or ItemID.BottomlessLavaBucket => 1,
                    ItemID.HoneyBucket or ItemID.BottomlessHoneyBucket => 2, ItemID.BottomlessShimmerBucket => 3, _ => -1 };
                if (fill >= 0) return evidence.X == x && evidence.Y == y && change.RemovedUnits == 0
                    && (change.PlacedUnits == 0 || (int)change.PlacedKind == fill);
                int absorb = tool switch { ItemID.SuperAbsorbantSponge => 0, ItemID.LavaAbsorbantSponge => 1,
                    ItemID.HoneyAbsorbantSponge => 2, _ => -1 };
                return change.PlacedUnits == 0 && (change.RemovedUnits == 0 || root.Units > 0
                    && change.RemovedKind == root.Kind && (absorb < 0 || (int)change.RemovedKind == absorb)
                    && (tool != ItemID.EmptyBucket || change.RemovedKind != LiquidKind.Shimmer));
            }
        }
    }
}
