using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>为客户端施工提供有界窗口和真实原版入站状态验证，不接收客户端统计数量。</summary>
    internal sealed partial class ImproveGameMultiplayerSystem
    {
        private const int MaximumCells = 8192;
        private const int MaximumTargets = MaximumCells * 3;
        private const int TargetsPerFragment = 256;
        [ThreadStatic] private static ClientStep currentClientStep;
        [ThreadStatic] private static ServerObservation currentObservation;
        private static ulong nextClientOperation;

        /// <summary>在受信拆除方法返回后同步空缺，确保上游原版拆除包先发出并保留服务器掉落。</summary>
        internal static void FlushClientRemovals() => currentClientStep?.FlushRemovals();

        /// <summary>直接拆墙后即将放新墙时，先同步仍然真实存在的空缺。</summary>
        private static void FlushBeforeWall(On_WorldGen.orig_PlaceWall orig, int x, int y, int type, bool mute)
        { FlushClientRemovals(); orig(x, y, type, mute); }

        /// <summary>直接拆块后即将放新块时保留中间状态，不写入已经不存在的历史快照。</summary>
        private static bool FlushBeforeTile(On_WorldGen.orig_PlaceTile orig, int x, int y, int type, bool mute, bool forced, int player, int style)
        { FlushClientRemovals(); return orig(x, y, type, mute, forced, player, style); }

        /// <summary>捕获原版入站内提前联动移除的格位；候选在结束时仍按精确目标过滤。</summary>
        private static void ObserveAcceptedKillTile(On_WorldGen.orig_KillTile orig, int x, int y, bool fail, bool effectOnly, bool noItem)
        { currentObservation?.Observe(x, y); orig(x, y, fail, effectOnly, noItem); }

        /// <summary>只在真实原版入站处理期间保存墙的破坏前状态，不给普通环境更新归属。</summary>
        private static void ObserveAcceptedKillWall(On_WorldGen.orig_KillWall orig, int x, int y, bool fail)
        { currentObservation?.Observe(x, y); orig(x, y, fail); }

        /// <summary>发布构造/建造采集入口的实际安装结果，与其他组互不覆盖。</summary>
        internal static void PublishConstruction(bool construct, bool building)
        {
            clientCapabilities &= ~(ImproveGameCapability.Construct | ImproveGameCapability.Building);
            if (construct) clientCapabilities |= ImproveGameCapability.Construct;
            if (building) clientCapabilities |= ImproveGameCapability.Building;
        }

        /// <summary>发布已审计固定拆除与机器人入口。</summary>
        internal static void PublishClientTools(bool fixedBurst, bool robot)
        {
            clientCapabilities &= ~(ImproveGameCapability.FixedBurst | ImproveGameCapability.Robot);
            if (fixedBurst) clientCapabilities |= ImproveGameCapability.FixedBurst;
            if (robot) clientCapabilities |= ImproveGameCapability.Robot;
        }

        /// <summary>只允许原始本地角色在已协商能力中创建候选；预测本身不能直接记账。</summary>
        internal static bool CanUseClient(Player player, ImproveGameCapability capability) => Main.netMode == NetmodeID.MultiplayerClient
            && !Main.gameMenu && !Main.dedServ && IsPlayer(player) && player.whoAmI == Main.myPlayer
            && cursor.Server != Guid.Empty && (cursor.Capabilities & clientCapabilities & capability) != 0;

        /// <summary>同步步骤内打开候选收集；同一玩家的嵌套调用复用当前窗口。</summary>
        internal static ClientStep BeginClientStep(Player player, ImproveGameCapability capability)
            => !CanUseClient(player, capability) || currentClientStep != null || nextClientOperation == ulong.MaxValue
                ? null : new ClientStep(player, capability, ++nextClientOperation);

        /// <summary>客户端同步窗口；首次目标前发开始标记，完成时按直接目标分片并提交。</summary>
        internal sealed class ClientStep : IDisposable, ITileChangeCandidateObserver
        {
            private readonly Player player;
            private readonly ImproveGameCapability capability;
            private readonly ulong operation;
            private readonly long world = TileChangeScope.WorldSession;
            private readonly Guid client = cursor.Client, server = cursor.Server;
            private readonly TileChangeScope scope;
            private readonly HashSet<(int X, int Y, TileChangeLayer Layer, int Type, int Style)> checkpoints = new();
            private readonly List<(int X, int Y, TileChangeLayer Layer, TileContentSnapshot Before)> removals = new();
            private bool started, disposed;
            private bool IsCurrent => world == TileChangeScope.WorldSession && client == cursor.Client && server == cursor.Server
                && CanUseClient(player, capability);

            /// <summary>保存玩家和会话，创建只产出候选的直接目标作用域。</summary>
            internal ClientStep(Player player, ImproveGameCapability capability, ulong operation)
            {
                this.player = player; this.capability = capability; this.operation = operation;
                currentClientStep = this;
                scope = TileChangeScope.BeginCandidate(player, this);
            }

            /// <summary>同步步骤退出时即使异常也提交已发生的实际结果；世界失效时不发送旧候选。</summary>
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try { scope.Dispose(); }
                finally { if (currentClientStep == this) currentClientStep = null; }
            }

            /// <summary>只有第一个直接目标才真正发包，空步骤没有网络请求。</summary>
            public void Observe()
            {
                if (started || !IsCurrent) return;
                started = true;
                Send(StatisticsPacketType.ImproveGameClientBegin, writer =>
                { ImproveGameProtocol.WriteSession(writer, client, server); writer.Write(operation); writer.Write((ushort)capability); });
            }

            /// <summary>先保存待同步空缺；底层 Kill 尚未返回原版 PickTile，不能抢在原版拆除包之前发送。</summary>
            public void Removed(int x, int y, TileChangeLayer layer, TileChangeIntent intent, TileContentSnapshot before)
            {
                if (!started || !IsCurrent) return;
                // 独立清理/固定拆除在整个步骤结束时批量同步空缺，不为每格额外发包。
                // 建造窗口可能同格清理后重建，替换也可能同类型重放，必须保留即时中间边界。
                if (intent == TileChangeIntent.Remove && capability != ImproveGameCapability.Building) return;
                int left = before.IsObject && layer == TileChangeLayer.Tile ? before.Origin.X : x;
                int top = before.IsObject && layer == TileChangeLayer.Tile ? before.Origin.Y : y;
                if (!checkpoints.Add((left, top, layer, before.TileType, before.Style))) return;
                removals.Add((x, y, layer, before));
            }

            /// <summary>只同步当前仍为空的区域；已被重建的内容不能伪造为空格补发。</summary>
            internal void FlushRemovals()
            {
                if (!IsCurrent) { removals.Clear(); return; }
                foreach (var removal in removals)
                {
                    var before = removal.Before;
                    int left = before.IsObject && removal.Layer == TileChangeLayer.Tile ? before.Origin.X : removal.X;
                    int top = before.IsObject && removal.Layer == TileChangeLayer.Tile ? before.Origin.Y : removal.Y;
                    int width = before.IsObject && removal.Layer == TileChangeLayer.Tile ? before.Width : 1;
                    int height = before.IsObject && removal.Layer == TileChangeLayer.Tile ? before.Height : 1;
                    if ((long)width * height > MaximumCells) continue;
                    bool empty = true;
                    for (int x = 0; x < width && empty; x++) for (int y = 0; y < height; y++)
                    {
                        var current = TileContentSnapshot.Capture(left + x, top + y);
                        if (!current.IsValid || (removal.Layer == TileChangeLayer.Tile ? current.HasTile : current.WallType != 0))
                        { empty = false; break; }
                    }
                    if (empty) SyncArea(left, top, width, height);
                }
                removals.Clear();
            }

            /// <summary>先同步每个完成步骤的真实最终状态，再发送精确候选；上游材料和调度不变。</summary>
            public void Complete(IReadOnlyList<TileChangeCandidate> targets)
            {
                if (!started || !IsCurrent) return;
                FlushRemovals();
                if (targets.Count > MaximumTargets) { Commit(-1); return; }
                var cells = new HashSet<(int X, int Y)>();
                var objects = new HashSet<(int X, int Y, int Width, int Height)>();
                foreach (TileChangeCandidate target in targets)
                {
                    cells.Add((target.X, target.Y));
                    if (target.Layer == TileChangeLayer.Tile)
                    {
                        AddObjectCells(cells, target.Before); AddObjectCells(cells, target.After);
                        if (target.After.IsObject)
                        {
                            if (target.After.Width > 32 || target.After.Height > 32) { Commit(-1); return; }
                            objects.Add((target.After.Origin.X, target.After.Origin.Y, target.After.Width, target.After.Height));
                        }
                    }
                    if (cells.Count > MaximumCells) { Commit(-1); return; }
                }
                // 家具完整同步，其他目标只合并相邻行段，不写入模板空洞或范围外的格位。
                foreach (var value in objects)
                {
                    SyncArea(value.X, value.Y, value.Width, value.Height);
                    for (int x = 0; x < value.Width; x++) for (int y = 0; y < value.Height; y++) cells.Remove((value.X + x, value.Y + y));
                }
                var ordered = cells.OrderBy(point => point.Y).ThenBy(point => point.X).ToList();
                for (int first = 0; first < ordered.Count;)
                {
                    var start = ordered[first]; int count = 1;
                    while (first + count < ordered.Count && count < 32 && ordered[first + count].Y == start.Y
                        && ordered[first + count].X == start.X + count) count++;
                    SyncArea(start.X, start.Y, count, 1); first += count;
                }
                int fragment = 0;
                for (int offset = 0; offset < targets.Count; offset += TargetsPerFragment)
                {
                    int first = offset, count = Math.Min(TargetsPerFragment, targets.Count - offset), number = fragment++;
                    Send(StatisticsPacketType.ImproveGameClientTargets, writer =>
                    {
                        ImproveGameProtocol.WriteSession(writer, client, server);
                        writer.Write(operation); writer.Write(number); writer.Write((ushort)count);
                        for (int i = first; i < first + count; i++)
                        {
                            var target = targets[i];
                            writer.Write((short)target.X); writer.Write((short)target.Y);
                            writer.Write((byte)target.Layer); writer.Write((byte)target.Intent);
                            Stamp.From(target.Before, target.Layer).Write(writer);
                            Stamp.From(target.After, target.Layer).Write(writer);
                        }
                    });
                }
                Commit(targets.Count);
            }

            /// <summary>完整对象的同步边界覆盖整个占用范围，不把组成格分别计数。</summary>
            private static void AddObjectCells(HashSet<(int X, int Y)> cells, TileContentSnapshot value)
            {
                if (!value.IsObject || (long)value.Width * value.Height > MaximumCells) return;
                for (int x = 0; x < value.Width; x++) for (int y = 0; y < value.Height; y++)
                    cells.Add((value.Origin.X + x, value.Origin.Y + y));
            }

            /// <summary>结束或放弃窗口；放弃不上传数量，也不保留可重放的确认上下文。</summary>
            private void Commit(int count) => Send(StatisticsPacketType.ImproveGameClientCommit, writer =>
            { ImproveGameProtocol.WriteSession(writer, client, server); writer.Write(operation); writer.Write(count); });
        }

        /// <summary>按真实当前格位发送有界原版区域包；不修改世界内容或伪造已消失的中间状态。</summary>
        private static void SyncArea(int x, int y, int width, int height)
        {
            if (width < 1 || height < 1 || (long)width * height > MaximumCells) return;
            for (int dx = 0; dx < width; dx += 32) for (int dy = 0; dy < height; dy += 32)
            {
                int w = Math.Min(32, width - dx), h = Math.Min(32, height - dy);
                if (WorldGen.InWorld(x + dx, y + dy) && WorldGen.InWorld(x + dx + w - 1, y + dy + h - 1))
                    NetMessage.SendTileSquare(Main.myPlayer, x + dx, y + dy, w, h);
            }
        }

        /// <summary>只处理已握手连接的窗口协议；每个候选必须匹配本窗口的实际入站证据。</summary>
        private static void HandleClientWindow(StatisticsPacketType type, BinaryReader reader, ServerSession session)
        {
            if (!ImproveGameProtocol.ReadSession(reader, session.Client, session.Server)) return;
            ulong operation = reader.ReadUInt64();
            if (type == StatisticsPacketType.ImproveGameClientBegin)
            {
                var capability = (ImproveGameCapability)reader.ReadUInt16();
                int bits = (ushort)capability;
                if (operation == 0 || operation <= session.LastClientOperation || bits == 0 || (bits & (bits - 1)) != 0
                    || (session.Capabilities & ImproveGameProtocol.ClientCapabilities & capability) == 0
                    || reader.BaseStream.Position != reader.BaseStream.Length || session.NextTask == ulong.MaxValue) return;
                session.LastClientOperation = operation;
                session.Window = new ServerWindow(operation, new Authority(session, ++session.NextTask, capability), session);
                return;
            }
            ServerWindow window = session.Window;
            if (window == null || window.Operation != operation) return;
            if (type == StatisticsPacketType.ImproveGameClientTargets)
            {
                int fragment = reader.ReadInt32(), count = reader.ReadUInt16();
                if (fragment != window.NextFragment++ || count < 1 || count > TargetsPerFragment
                    || window.Candidates.Count + count > MaximumTargets) { window.Reject(); return; }
                for (int i = 0; i < count; i++)
                {
                    int x = reader.ReadInt16(), y = reader.ReadInt16();
                    var layer = (TileChangeLayer)reader.ReadByte(); var intent = (TileChangeIntent)reader.ReadByte();
                    var candidate = new Candidate(x, y, layer, intent, Stamp.Read(reader), Stamp.Read(reader));
                    if (!WorldGen.InWorld(x, y) || layer > TileChangeLayer.Wiring || intent > TileChangeIntent.Replace
                        || !window.Candidates.TryAdd((x, y, layer), candidate)) window.Reject();
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) window.Reject();
                return;
            }
            int expected = reader.ReadInt32();
            session.Window = null; // 先撤销身份，再结算；重复提交不能再次消费同一证据。
            if (expected < 0) window.Reject();
            if (expected < 0 || expected != window.Candidates.Count || window.Invalid
                || Main.GameUpdateCount - window.Created > 1800 || reader.BaseStream.Position != reader.BaseStream.Length) return;
            window.Confirm();
        }

        /// <summary>在原版入站处理前只快照该包实际涉及的小区域，不扫描整个世界或整个任务区域。</summary>
        private static ServerObservation BeginObservation(MessageBuffer self, int start, int length)
        {
            if (Main.netMode != NetmodeID.Server || inboundPlayer == null
                || !sessions.TryGetValue(inboundPlayer.Value, out ServerSession session) || session.Window == null) return null;
            ServerWindow window = session.Window;
            if (window.Invalid || Main.GameUpdateCount - window.Created > 1800) { window.Reject(); return null; }
            try
            {
                if (start < 0 || length < 1 || start > self.readBuffer.Length - length) return null;
                using var reader = new BinaryReader(new MemoryStream(self.readBuffer, start, length, writable: false));
                int message = reader.ReadByte(), x, y, width = 1, height = 1;
                switch (message)
                {
                    case 20: // 本机已审计 TileSquare：两个 Int16 坐标及 Byte 宽高。
                        x = reader.ReadInt16(); y = reader.ReadInt16(); width = reader.ReadByte(); height = reader.ReadByte(); break;
                    case 17:
                        reader.ReadByte(); x = reader.ReadInt16(); y = reader.ReadInt16(); break;
                    case 34: // 箱子/梳妆台放置与拆除；候选掩码仍只接收直接对象。
                        reader.ReadByte(); x = reader.ReadInt16() - 3; y = reader.ReadInt16() - 3; width = height = 7; break;
                    case 79:
                        x = reader.ReadInt16(); y = reader.ReadInt16(); int type = reader.ReadInt16(), style = reader.ReadInt16();
                        int alternate = reader.ReadByte(); var data = TileObjectData.GetTileData(type, style, alternate);
                        if (data == null) { window.Reject(); return null; }
                        x -= data.Origin.X; y -= data.Origin.Y; width = data.Width; height = data.Height; break;
                    default: return null;
                }
                if (width < 1 || height < 1 || (long)width * height > MaximumCells) { window.Reject(); return null; }
                var before = new Dictionary<(int X, int Y), TileContentSnapshot>();
                for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++)
                {
                    int i = x + dx, j = y + dy;
                    if (!WorldGen.InWorld(i, j)) continue;
                    if (before.ContainsKey((i, j))) continue;
                    TileContentSnapshot value = TileContentSnapshot.Capture(i, j); before.TryAdd((i, j), value);
                    if (value.IsObject)
                    {
                        if ((long)value.Width * value.Height > MaximumCells) { window.Reject(); return null; }
                        for (int ox = 0; ox < value.Width; ox++) for (int oy = 0; oy < value.Height; oy++)
                        {
                            int px = value.Origin.X + ox, py = value.Origin.Y + oy;
                            before.TryAdd((px, py), TileContentSnapshot.Capture(px, py));
                        }
                    }
                    if (before.Count > MaximumCells) { window.Reject(); return null; }
                }
                return new ServerObservation(window, before);
            }
            catch (Exception error) when (error is IOException || error is ArgumentException)
            { window.Reject(); return null; }
        }

        /// <summary>保存一次实际原版入站的两侧快照；只有该连接接受的变化能进入该窗口。</summary>
        private sealed class ServerObservation
        {
            private readonly ServerWindow window;
            private readonly Dictionary<(int X, int Y), TileContentSnapshot> before;
            private readonly Dictionary<(int X, int Y, int Type, int Style), bool> completeObjects = new();
            /// <summary>先保存家具完整性，避免包写入后再判断旧对象是否曾完整存在。</summary>
            internal ServerObservation(ServerWindow window, Dictionary<(int X, int Y), TileContentSnapshot> before)
            {
                this.window = window; this.before = before;
                foreach (var pair in before) Register(pair.Key.X, pair.Key.Y, pair.Value);
            }

            /// <summary>递归破坏发生前冻结目标及原始家具，避免后续区域包只能看到空格。</summary>
            internal void Observe(int x, int y)
            {
                if (window.Invalid || !WorldGen.InWorld(x, y) || before.ContainsKey((x, y))) return;
                TileContentSnapshot value = TileContentSnapshot.Capture(x, y);
                int width = value.IsObject ? value.Width : 1, height = value.IsObject ? value.Height : 1;
                int left = value.IsObject ? value.Origin.X : x, top = value.IsObject ? value.Origin.Y : y;
                if ((long)width * height > MaximumCells) { window.Reject(); return; }
                for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++)
                {
                    int i = left + dx, j = top + dy;
                    if (before.ContainsKey((i, j))) continue;
                    if (before.Count >= MaximumCells) { window.Reject(); return; }
                    TileContentSnapshot part = TileContentSnapshot.Capture(i, j);
                    before.Add((i, j), part); Register(i, j, part);
                }
            }

            /// <summary>登记一次真实破坏前的每层身份并检查跨连接变化污染。</summary>
            private void Register(int x, int y, TileContentSnapshot value)
            {
                if (window.Invalid) return;
                bool complete = IsComplete(value, completeObjects);
                for (int layer = 0; layer < 3; layer++)
                {
                    var key = (x, y, (TileChangeLayer)layer);
                    if (!window.Traces.TryGetValue(key, out Trace trace))
                    {
                        if (window.Traces.Count >= MaximumCells * 3) { window.Reject(); return; }
                        window.Traces.Add(key, new Trace(value, complete));
                    }
                    else if (Stamp.From(trace.Last, (TileChangeLayer)layer) != Stamp.From(value, (TileChangeLayer)layer))
                        trace.Conflicted = true; // 期间被另一玩家或环境改动，不能借用其结果。
                }
            }

            /// <summary>原版处理成功返回后才保存真实结果，失败包不被候选声明变成成功。</summary>
            internal void Complete()
            {
                if (window.Invalid) return;
                var removedObjects = new Dictionary<(int X, int Y, int Type, int Style), bool>();
                foreach (var pair in before)
                {
                    var after = TileContentSnapshot.Capture(pair.Key.X, pair.Key.Y);
                    for (int layer = 0; layer < 3; layer++)
                    {
                        var key = (pair.Key.X, pair.Key.Y, (TileChangeLayer)layer);
                        Trace trace = window.Traces[key];
                        trace.Changed |= Stamp.From(pair.Value, (TileChangeLayer)layer) != Stamp.From(after, (TileChangeLayer)layer);
                        trace.Last = after;
                        if (layer == (int)TileChangeLayer.Tile && trace.First.HasTile)
                        {
                            if (!trace.First.IsObject) trace.Removed |= !after.HasTile;
                            else
                            {
                                var identity = (trace.First.Origin.X, trace.First.Origin.Y, trace.First.TileType, trace.First.Style);
                                if (!removedObjects.TryGetValue(identity, out bool removed))
                                    removedObjects.Add(identity, removed = trace.First.IsFullyRemoved());
                                trace.Removed |= removed;
                            }
                        }
                        if (layer == (int)TileChangeLayer.Wall && trace.First.WallType != 0) trace.Removed |= after.WallType == 0;
                    }
                }
            }

            /// <summary>入站处理抛异常时放弃本窗口，不对部分解析作成功推断。</summary>
            internal void Invalidate() => window.Reject();
        }

        /// <summary>每连接最多一个同步施工窗口；记录来源、观察证据及最终直接候选。</summary>
        internal sealed class ServerWindow
        {
            internal readonly ulong Operation, Created = Main.GameUpdateCount;
            internal readonly Authority Authority;
            private readonly ServerSession session;
            internal readonly Dictionary<(int X, int Y, TileChangeLayer Layer), Trace> Traces = new();
            internal readonly Dictionary<(int X, int Y, TileChangeLayer Layer), Candidate> Candidates = new();
            internal bool Invalid;
            internal int NextFragment;
            /// <summary>窗口任务编号由服务器生成，不采用客户端操作号作为统计去重身份。</summary>
            internal ServerWindow(ulong operation, Authority authority, ServerSession session)
            { Operation = operation; Authority = authority; this.session = session; }

            /// <summary>有界验证失败时撤销窗口并对连接只诊断一次，不扩大候选匹配范围。</summary>
            internal void Reject()
            {
                Invalid = true;
                if (session.WarnedClientDrop) return;
                session.WarnedClientDrop = true;
                Terraria.ModLoader.ModContent.GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>().Logger.Warn(
                    "ImproveGame client-step verification was dropped: malformed, expired or over-budget window. No inferred credits were added.");
            }

            /// <summary>按真实接收链及完整对象结算，每一侧最多确认一次。</summary>
            internal void Confirm()
            {
                var seen = new HashSet<(TileChangeKind Kind, int X, int Y, int Type, int Style)>();
                var completeObjects = new Dictionary<(int X, int Y, int Type, int Style), bool>();
                foreach (var pair in Candidates)
                {
                    var key = pair.Key; Candidate candidate = pair.Value;
                    if (!Traces.TryGetValue(key, out Trace trace) || !trace.Changed || trace.Conflicted
                        || candidate.Before != Stamp.From(trace.First, key.Layer) || candidate.After != Stamp.From(trace.Last, key.Layer)
                        || candidate.After != Stamp.From(TileContentSnapshot.Capture(key.X, key.Y), key.Layer)) continue;
                    var before = trace.First; var after = trace.Last;
                    if (key.Layer == TileChangeLayer.Wiring)
                    {
                        for (int i = 0; i < 5; i++) if (((before.Wires ^ after.Wires) & (1 << i)) != 0)
                            Authority.Record(new TileChangeResult((after.Wires & (1 << i)) != 0 ? TileChangeKind.WiringPlaced : TileChangeKind.WiringRemoved, i));
                    }
                    else if (key.Layer == TileChangeLayer.Wall)
                    {
                        if (before.WallType == after.WallType && !trace.Removed) continue;
                        if (before.WallType != 0) Authority.Record(new TileChangeResult(TileChangeKind.WallRemoved, before.WallType));
                        if (candidate.Intent != TileChangeIntent.Remove && after.WallType != 0)
                            Authority.Record(new TileChangeResult(TileChangeKind.WallPlaced, after.WallType));
                    }
                    else
                    {
                        if (before.SameContent(after) && !trace.Removed) continue;
                        if (before.HasTile && before.HasKnownIdentity && (!before.IsObject || (trace.FirstComplete && trace.Removed)))
                            Submit(before, false, key.X, key.Y, seen);
                        if (candidate.Intent != TileChangeIntent.Remove && IsComplete(after, completeObjects)) Submit(after, true, key.X, key.Y, seen);
                    }
                }
            }

            /// <summary>家具按原点与规范样式去重，普通物块按格位去重。</summary>
            private void Submit(TileContentSnapshot value, bool placed, int x, int y,
                HashSet<(TileChangeKind Kind, int X, int Y, int Type, int Style)> seen)
            {
                TileChangeKind kind = value.IsObject ? (placed ? TileChangeKind.ObjectPlaced : TileChangeKind.ObjectRemoved)
                    : (placed ? TileChangeKind.TilePlaced : TileChangeKind.TileRemoved);
                int style = value.IsObject ? value.Style : 0;
                if (seen.Add((kind, value.IsObject ? value.Origin.X : x, value.IsObject ? value.Origin.Y : y, value.TileType, style)))
                    Authority.Record(new TileChangeResult(kind, value.TileType, style));
            }
        }

        /// <summary>仅保存由本连接原版入站实际观察的值，不保留可变 Tile 引用。</summary>
        internal sealed class Trace
        {
            internal readonly TileContentSnapshot First;
            internal readonly bool FirstComplete;
            internal TileContentSnapshot Last;
            internal bool Removed, Changed, Conflicted;
            /// <summary>初次观察时冻结真实旧内容与完整性。</summary>
            internal Trace(TileContentSnapshot value, bool complete) { First = Last = value; FirstComplete = complete; }
        }

        /// <summary>在同一不可重入观察边界内按对象缓存完整性，避免多格对象每个组成格反复遍历整件家具。</summary>
        private static bool IsComplete(TileContentSnapshot value, Dictionary<(int X, int Y, int Type, int Style), bool> cache)
        {
            if (!value.IsObject) return value.IsFullyPresent();
            var identity = (value.Origin.X, value.Origin.Y, value.TileType, value.Style);
            if (!cache.TryGetValue(identity, out bool complete)) cache.Add(identity, complete = value.IsFullyPresent());
            return complete;
        }

        /// <summary>定义只含类型、样式、对象身份及组件位的有界内容指纹，忽略颜色和坡形。</summary>
        internal readonly record struct Stamp(int Type, int Style, short X, short Y, ushort Width, ushort Height, byte Wires)
        {
            /// <summary>根据目标层取得对应内容身份。</summary>
            internal static Stamp From(TileContentSnapshot value, TileChangeLayer layer) => layer switch
            {
                TileChangeLayer.Wall => new Stamp(value.WallType, 0, 0, 0, 0, 0, 0),
                TileChangeLayer.Wiring => new Stamp(0, 0, 0, 0, 0, 0, value.Wires),
                _ => !value.HasTile ? new Stamp(-1, 0, 0, 0, 0, 0, 0) : value.IsObject
                    ? new Stamp(value.TileType, value.Style, (short)value.Origin.X, (short)value.Origin.Y, (ushort)value.Width, (ushort)value.Height, 0)
                    : new Stamp(value.TileType, -1, 0, 0, 0, 0, 0)
            };
            /// <summary>固定宽度写入，单候选最多四十字节，不允许任意长度分配。</summary>
            internal void Write(BinaryWriter writer)
            { writer.Write(Type); writer.Write(Style); writer.Write(X); writer.Write(Y); writer.Write(Width); writer.Write(Height); writer.Write(Wires); }
            /// <summary>指纹只用于比较，不能直接生成统计；所有字段必须匹配服务器值。</summary>
            internal static Stamp Read(BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadByte());
        }

        /// <summary>候选只有精确坐标、层、语义与两侧身份，没有客户端申报的统计增量。</summary>
        internal readonly record struct Candidate(int X, int Y, TileChangeLayer Layer, TileChangeIntent Intent, Stamp Before, Stamp After);
    }
}
