using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Statistics;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// M4 握手、真实入站发送者绑定、服务器任务与客户端施工确认及有界结果回传。
    /// 不把最终区域净差或客户端自报数量当作中间拆放证据。
    /// </summary>
    internal sealed partial class ImproveGameMultiplayerSystem : ModSystem
    {
        private readonly List<Hook> hooks = new();
        private static ImproveGameCapability collectionCapabilities, receiverCapabilities;
        private static ImproveGameCapability clientCapabilities;
        private static readonly Dictionary<int, ServerSession> sessions = new();
        private static readonly ImproveGameCreditCursor cursor = new();
        [ThreadStatic] private static int? inboundPlayer;
        [ThreadStatic] private static Authority active;
        private static ulong lastHello;
        private static int helloAttempts;
        private static bool warnedMissingServer;
        internal static ImproveGameCapability Capabilities => (collectionCapabilities & receiverCapabilities) | clientCapabilities;

        /// <summary>只在玩家打开的界面中查询真实安装/协商状态，不读取或创建任何 ModPlayer 数据。</summary>
        internal static ImproveGameCompatibilityStatus GetDisplayStatus()
        {
            bool installed = ModLoader.TryGetMod("ImproveGame", out Mod mod);
            return ImproveGameCompatibilityStatus.Create(installed ? mod.Version.ToString() : null,
                installed && ImproveGameVersionPolicy.Supports(mod.Version),
                Main.netMode == NetmodeID.MultiplayerClient, collectionCapabilities | clientCapabilities, Capabilities,
                cursor.Server != Guid.Empty, helloAttempts >= 10, cursor.Capabilities);
        }

        public override void Load()
        {
            On_MessageBuffer.GetData += HandleMessage;
            On_WorldGen.KillTile += ObserveAcceptedKillTile;
            On_WorldGen.KillWall += ObserveAcceptedKillWall;
            On_WorldGen.PlaceTile += FlushBeforeTile;
            On_WorldGen.PlaceWall += FlushBeforeWall;
        }

        /// <summary>由 M3 在全部叶子采集 Hook 安装完成后调用，防止先编译接收入口而内联未挂接的操作。</summary>
        internal void InstallReceivers(Mod mod)
        {
            if (hooks.Count != 0 || !ImproveGameVersionPolicy.Supports(mod.Version)) return;
            Assembly code = mod.Code;
            InstallReceiver(code, "ImproveGame.Content.Packets.SpaceWandOperation", ImproveGameCapability.Space);
            InstallReceiver(code, "ImproveGame.Content.Packets.SelectorItemOperation",
                ImproveGameCapability.BurstSelector | ImproveGameCapability.WallSelector);
            InstallReceiver(code, "ImproveGame.Packets.Tiles.VeinMiningPacket", ImproveGameCapability.Vein);
            Mod.Logger.Info($"ImproveGame M4 receiver hooks installed: {receiverCapabilities}. "
                + "Recording requires matching protocol and mutually negotiated capabilities.");
        }

        public override void OnWorldLoad() => ResetWorld();
        public override void OnWorldUnload() => ResetWorld();

        public override void Unload()
        {
            On_MessageBuffer.GetData -= HandleMessage;
            On_WorldGen.KillTile -= ObserveAcceptedKillTile;
            On_WorldGen.KillWall -= ObserveAcceptedKillWall;
            On_WorldGen.PlaceTile -= FlushBeforeTile;
            On_WorldGen.PlaceWall -= FlushBeforeWall;
            for (int i = hooks.Count - 1; i >= 0; i--) hooks[i].Dispose();
            hooks.Clear(); collectionCapabilities = receiverCapabilities = 0;
            clientCapabilities = 0;
            ResetWorld();
        }

        public override void PostUpdatePlayers()
        {
            if (Main.netMode == NetmodeID.Server)
            {
                var expired = new List<int>();
                foreach (var pair in sessions)
                {
                    if (!IsPlayer(pair.Value.Player)) expired.Add(pair.Key);
                    else if (pair.Value.Window != null && Main.GameUpdateCount - pair.Value.Window.Created > 1800)
                    { pair.Value.Window.Reject(); pair.Value.Window = null; }
                }
                foreach (int index in expired) sessions.Remove(index);
                return;
            }
            if (Main.netMode != NetmodeID.MultiplayerClient || Main.gameMenu || Main.LocalPlayer?.active != true) return;
            // 不依赖 ModPlayer 与 ModSystem 世界回调的相对顺序；仅在实际游戏更新中补建本地挑战。
            if (cursor.Client == Guid.Empty) EnterWorld();
            if (cursor.Server != Guid.Empty || Capabilities == 0 || helloAttempts >= 10) return;
            if (helloAttempts != 0 && Main.GameUpdateCount - lastHello < 180) return;
            lastHello = Main.GameUpdateCount; helloAttempts++;
            Send(StatisticsPacketType.ImproveGameHello, packet =>
            { packet.Write(ImproveGameProtocol.Version); packet.Write(cursor.Client.ToByteArray()); packet.Write((byte)Capabilities); });
            if (helloAttempts == 10 && !warnedMissingServer)
            {
                warnedMissingServer = true;
                Mod.Logger.Warn("ImproveGame multiplayer statistics handshake has no matching server reply; new tool credits remain disabled.");
            }
        }

        public override void PostUpdateWorld()
        {
            if (Main.netMode != NetmodeID.Server) return;
            foreach (ServerSession session in sessions.Values)
            {
                if (!session.Ready || !IsPlayer(session.Player)) continue;
                // 每连接每帧最多八包，每包至多 24 条聚合项，连同最长内容名仍小于 32 KiB。
                for (int batch = 0; batch < 8 && session.Pending.Count > 0; batch++)
                {
                    ulong task = 0;
                    var entries = new List<ImproveGameCredit>();
                    var removed = new List<PendingKey>();
                    foreach (var pair in session.Pending)
                    {
                        if (Main.GameUpdateCount - pair.Value.Tick > 1800)
                        { WarnExpired(session); removed.Add(pair.Key); continue; }
                        if (task == 0) task = pair.Key.Task;
                        if (task != pair.Key.Task) continue;
                        entries.Add(new ImproveGameCredit(pair.Key.Kind, pair.Key.Content, pair.Key.Style, pair.Value.Count));
                        removed.Add(pair.Key);
                        if (entries.Count == ImproveGameProtocol.MaxEntries) break;
                    }
                    foreach (PendingKey key in removed) session.Pending.Remove(key);
                    if (entries.Count == 0) continue;
                    if (session.Sequence == ulong.MaxValue) { session.Ready = false; session.Pending.Clear(); break; }
                    ulong sequence = ++session.Sequence;
                    Send(StatisticsPacketType.ImproveGameCreditBatch, packet => ImproveGameProtocol.WriteBatch(
                        packet, session.Client, session.Server, task, sequence, entries), session.Player.whoAmI);
                }
            }
        }

        /// <summary>发布实际安装成功的采集组；最终能力取采集与网络入口的交集。</summary>
        internal static void PublishCapabilities(bool space, bool burst, bool wall, bool vein)
        {
            collectionCapabilities = (space ? ImproveGameCapability.Space : 0)
                | (burst ? ImproveGameCapability.BurstSelector : 0) | (wall ? ImproveGameCapability.WallSelector : 0)
                | (vein ? ImproveGameCapability.Vein : 0);
        }

        /// <summary>本地角色进入世界时生成新挑战；返回菜单或换世界后旧包一律失效。</summary>
        internal static void EnterWorld()
        {
            cursor.Reset(Main.netMode == NetmodeID.MultiplayerClient ? Guid.NewGuid() : Guid.Empty);
            nextClientOperation = 0;
            lastHello = 0; helloAttempts = 0; warnedMissingServer = false;
        }

        /// <summary>服务器断线事件立即撤销连接身份及所有尚未回传的结果。</summary>
        internal static void Disconnect(int player) => sessions.Remove(player);

        /// <summary>清除会话、队列和当前入口；任务弱关联中的旧 Authority 将因此失效。</summary>
        private static void ResetWorld()
        { sessions.Clear(); cursor.Reset(Guid.Empty); inboundPlayer = null; active = null; helloAttempts = 0; currentClientStep = null; currentObservation = null; nextClientOperation = 0; }

        /// <summary>严格匹配实际网络接收入口及 Sender 属性，能力失败时只关闭相应组。</summary>
        private void InstallReceiver(Assembly code, string name, ImproveGameCapability capability)
        {
            try
            {
                hooks.Add(CreateReceiverHook(code, name, capability));
                receiverCapabilities |= capability;
            }
            catch (Exception error) { Mod.Logger.Warn($"ImproveGame M4 {name} disabled: {error.Message}"); }
        }

        /// <summary>创建可独立审计的接收 Hook；失败原样向能力安装器报告，不掩盖原始异常。</summary>
        private static Hook CreateReceiverHook(Assembly code, string name, ImproveGameCapability capability)
        {
            Type type = code.GetType(name) ?? throw new TypeLoadException(name);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo sender = type.GetProperty("Sender", flags);
            MethodInfo receive = type.GetMethod("Receive", flags | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (sender?.PropertyType != typeof(int) || sender.GetMethod == null || receive?.ReturnType != typeof(void))
                throw new MissingMethodException(name, "Receive/Sender");
            return new Hook(receive, (Action<Action<object>, object>)((orig, packet) =>
                ReceiveOperation(orig, packet, (int)sender.GetValue(packet), capability)));
        }

        /// <summary>在第三方网络层之外取得真实连接，不信任模组包内自报的玩家编号。</summary>
        private static void HandleMessage(On_MessageBuffer.orig_GetData orig, MessageBuffer self, int start, int length, out int type)
        {
            int? previous = inboundPlayer;
            inboundPlayer = Main.netMode == NetmodeID.Server && self != null && self.whoAmI >= 0
                && self.whoAmI < Main.player.Length && IsPlayer(Main.player[self.whoAmI]) ? self.whoAmI : null;
            ServerObservation observation = BeginObservation(self, start, length);
            ServerObservation previousObservation = currentObservation;
            currentObservation = observation;
            try { orig(self, start, length, out type); observation?.Complete(); }
            catch { observation?.Invalidate(); throw; }
            finally { currentObservation = previousObservation; inboundPlayer = previous; }
        }

        /// <summary>真实发送者与第三方 Sender 一致且握手完成后，才创建服务器分配的任务身份。</summary>
        private static void ReceiveOperation(Action<object> orig, object packet, int sender, ImproveGameCapability capability)
        {
            Authority previous = active;
            active = null;
            if (Main.netMode == NetmodeID.Server && inboundPlayer == sender && sessions.TryGetValue(sender, out ServerSession session)
                && session.Ready && IsPlayer(session.Player) && (session.Capabilities & capability) != 0
                && session.NextTask < ulong.MaxValue)
                active = new Authority(session, ++session.NextTask, capability);
            try { orig(packet); }
            finally { active = previous; }
        }

        /// <summary>只在经过确认的网络入口创建任务；后续协程保留该身份，不读取当前手持。</summary>
        internal static Authority Capture(Player player, ImproveGameCapability capability)
            => active?.Allows(player, capability) == true ? active : null;

        /// <summary>接收三步握手与服务器结果；客户端不能通过该入口上传施工数量。</summary>
        internal static void HandlePacket(StatisticsPacketType type, BinaryReader reader, int whoAmI)
        {
            try
            {
                // tModLoader 共用 MessageBuffer 流的 Length 不是当前模组包边界，先提取有界正文。
                int length = reader.ReadUInt16();
                if (length < 1 || length > ImproveGameProtocol.MaxBodyBytes) return;
                byte[] body = reader.ReadBytes(length);
                if (body.Length != length) return;
                using var stream = new MemoryStream(body, writable: false);
                using var bounded = new BinaryReader(stream);
                HandleBody(type, bounded, whoAmI);
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is FormatException)
            {
                if (Main.netMode == NetmodeID.Server && type >= StatisticsPacketType.ImproveGameClientBegin
                    && sessions.TryGetValue(whoAmI, out ServerSession session)) session.Window?.Reject();
            }
        }

        /// <summary>仅解释已按显式长度提取的正文，原子解析后才修改会话或数据。</summary>
        private static void HandleBody(StatisticsPacketType type, BinaryReader reader, int whoAmI)
        {
            if (Main.netMode == NetmodeID.Server && whoAmI >= 0 && whoAmI < Main.player.Length && IsPlayer(Main.player[whoAmI]))
            {
                if (type == StatisticsPacketType.ImproveGameHello)
                {
                    if (reader.ReadUInt16() != ImproveGameProtocol.Version) return;
                    Guid client = ImproveGameProtocol.ReadIdentity(reader);
                    var offered = (ImproveGameCapability)reader.ReadByte();
                    if ((offered & ~ImproveGameProtocol.All) != 0 || reader.BaseStream.Position != reader.BaseStream.Length) return;
                    if (!sessions.TryGetValue(whoAmI, out ServerSession session) || session.Client != client
                        || session.Player != Main.player[whoAmI])
                    {
                        session = new ServerSession(Main.player[whoAmI], client, offered & Capabilities);
                        sessions[whoAmI] = session;
                    }
                    Send(StatisticsPacketType.ImproveGameWelcome, response =>
                    { ImproveGameProtocol.WriteSession(response, session.Client, session.Server);
                        response.Write((byte)session.Capabilities); }, whoAmI);
                }
                else if (type == StatisticsPacketType.ImproveGameReady && sessions.TryGetValue(whoAmI, out ServerSession ready)
                    && ImproveGameProtocol.ReadSession(reader, ready.Client, ready.Server)
                    && reader.BaseStream.Position == reader.BaseStream.Length) ready.Ready = true;
                else if (type >= StatisticsPacketType.ImproveGameClientBegin && type <= StatisticsPacketType.ImproveGameClientCommit
                    && sessions.TryGetValue(whoAmI, out ServerSession clientSession) && clientSession.Ready)
                    HandleClientWindow(type, reader, clientSession);
                return;
            }
            if (Main.netMode != NetmodeID.MultiplayerClient || Main.gameMenu || cursor.Client == Guid.Empty) return;
            if (type == StatisticsPacketType.ImproveGameWelcome)
            {
                if (reader.ReadUInt16() != ImproveGameProtocol.Version) return;
                Guid client = ImproveGameProtocol.ReadIdentity(reader), server = ImproveGameProtocol.ReadIdentity(reader);
                var capabilities = (ImproveGameCapability)reader.ReadByte();
                if (reader.BaseStream.Position != reader.BaseStream.Length || (capabilities & ~Capabilities) != 0
                    || !cursor.Welcome(client, server, capabilities)) return;
                Send(StatisticsPacketType.ImproveGameReady, response => ImproveGameProtocol.WriteSession(response, client, server));
                ModContent.GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>().Logger.Info(
                    $"ImproveGame multiplayer statistics negotiated: {capabilities}; protocol {ImproveGameProtocol.Version}.");
            }
            else if (type == StatisticsPacketType.ImproveGameCreditBatch && cursor.Server != Guid.Empty
                && ImproveGameProtocol.TryReadBatch(reader, cursor.Client, cursor.Server, out ulong sequence, out var entries))
            {
                if (Main.LocalPlayer?.active != true) return;
                foreach (ImproveGameCredit entry in entries)
                {
                    if (entry.Kind >= TileChangeKind.WiringPlaced)
                    { if (!ImproveGameProtocol.TryGetWiringComponent(entry.Content, out _)) return; continue; }
                    bool wall = entry.Kind == TileChangeKind.WallPlaced || entry.Kind == TileChangeKind.WallRemoved;
                    if (!(wall ? entry.Content.TryResolveWallType(out _) : entry.Content.TryResolveTileType(out _))) return;
                }
                if (!cursor.Advance(sequence)) return;
                StatisticsPlayer statistics = Main.LocalPlayer.GetModPlayer<StatisticsPlayer>();
                foreach (ImproveGameCredit entry in entries) statistics.RecordConfirmedConstruction(entry);
            }
        }

        /// <summary>创建带明确类型的模组包，复用既有分派但不复用旧包编号。</summary>
        private static void Send(StatisticsPacketType type, Action<BinaryWriter> write, int to = -1)
        {
            using var body = new MemoryStream();
            using (var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true)) write(writer);
            if (body.Length < 1 || body.Length > ImproveGameProtocol.MaxBodyBytes)
                throw new InvalidDataException("Compatibility packet exceeds frame budget.");
            ModPacket packet = ModContent.GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>().GetPacket();
            packet.Write((byte)type); packet.Write((ushort)body.Length); packet.Write(body.ToArray()); packet.Send(to);
        }

        /// <summary>验证在线玩家实例，服务器路径绝不访问 LocalPlayer。</summary>
        private static bool IsPlayer(Player player) => player?.active == true && player.whoAmI >= 0
            && player.whoAmI < Main.player.Length && Main.player[player.whoAmI] == player;

        /// <summary>对同一连接的超时降级只提示一次，避免长任务反复刷日志。</summary>
        private static void WarnExpired(ServerSession session)
        {
            if (session.WarnedExpiry) return;
            session.WarnedExpiry = true;
            ModContent.GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>().Logger.Warn(
                "ImproveGame multiplayer task/credit expired after 30 seconds without progress; unconfirmed results were discarded.");
        }

        /// <summary>绑定连接、任务和能力；在每次推进时检查会话仍有效，断线和换世界立即失效。</summary>
        internal sealed class Authority
        {
            private readonly ServerSession session;
            private readonly ulong task;
            private readonly ImproveGameCapability capability;
            private ulong lastActivity;

            /// <summary>由真实接收入口分配；客户端不能自行创建任务编号。</summary>
            internal Authority(ServerSession session, ulong task, ImproveGameCapability capability)
            { this.session = session; this.task = task; this.capability = capability; lastActivity = Main.GameUpdateCount; }

            /// <summary>只允许本任务能力和原始玩家，任务超过三十秒无活动后不重新归属。</summary>
            internal bool Allows(Player player, ImproveGameCapability requested)
            {
                bool valid = Main.netMode == NetmodeID.Server && !Main.gameMenu && session.Ready && player == session.Player
                    && IsPlayer(player) && sessions.TryGetValue(player.whoAmI, out ServerSession current) && current == session
                    && (session.Capabilities & capability & requested) != 0;
                if (!valid) return false;
                if (Main.GameUpdateCount - lastActivity <= 1800) return true;
                WarnExpired(session); return false;
            }

            /// <summary>每个有效实际步骤续期，返回仅用于远程结果的共用结算器。</summary>
            internal TileChangeScope Begin(Player player)
            {
                if (!Allows(player, capability)) return TileChangeScope.Suppress();
                lastActivity = Main.GameUpdateCount;
                return TileChangeScope.BeginVerified(player, Record);
            }

            /// <summary>聚合真实服务器结果；队列和数量超限时丢弃并只记录一次诊断。</summary>
            internal void Record(TileChangeResult result)
            {
                if (!Allows(session.Player, capability)) return;
                bool wall = result.Kind == TileChangeKind.WallPlaced || result.Kind == TileChangeKind.WallRemoved;
                int type = result.Type;
                if (result.Kind == TileChangeKind.ObjectPlaced || result.Kind == TileChangeKind.ObjectRemoved)
                    type = type switch { TileID.OpenDoor => TileID.ClosedDoor, TileID.TrapdoorOpen => TileID.TrapdoorClosed,
                        TileID.TallGateOpen => TileID.TallGateClosed, _ => type };
                StableContentKey content;
                if (result.Kind >= TileChangeKind.WiringPlaced)
                {
                    string name = type switch { 0 => "WireRed", 1 => "WireBlue", 2 => "WireGreen", 3 => "WireYellow", 4 => "Actuator", _ => null };
                    if (name == null) return;
                    content = new StableContentKey("Terraria", name);
                }
                else if (!(wall ? StableContentKey.TryCreateForWallType(type, out content) : StableContentKey.TryCreateForTileType(type, out content))) return;
                var credit = new ImproveGameCredit(result.Kind, content, result.Style, 1);
                if (!ImproveGameProtocol.IsValid(credit)) return;
                var key = new PendingKey(task, result.Kind, content, result.Style);
                session.Pending.TryGetValue(key, out PendingValue value);
                if ((value.Count == 0 && session.Pending.Count >= ImproveGameProtocol.MaxPending) || value.Count >= ImproveGameProtocol.MaxCount)
                {
                    if (!session.Warned)
                    {
                        session.Warned = true;
                        ModContent.GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>().Logger.Warn(
                            "ImproveGame multiplayer credit queue limit reached; unconfirmed overflow was discarded.");
                    }
                    return;
                }
                session.Pending[key] = new PendingValue(value.Count + 1, Main.GameUpdateCount);
            }
        }

        /// <summary>存放有界聚合队列及连接随机身份，不保存个人生涯或世界切片。</summary>
        internal sealed class ServerSession
        {
            internal readonly Player Player;
            internal readonly Guid Client, Server = Guid.NewGuid();
            internal readonly ImproveGameCapability Capabilities;
            internal readonly Dictionary<PendingKey, PendingValue> Pending = new();
            internal bool Ready, Warned, WarnedExpiry, WarnedClientDrop;
            internal ulong NextTask, Sequence;
            internal ulong LastClientOperation;
            internal ServerWindow Window;
            /// <summary>每个新的角色连接挑战建立全新服务器会话。</summary>
            internal ServerSession(Player player, Guid client, ImproveGameCapability capabilities)
            { Player = player; Client = client; Capabilities = capabilities; }
        }

        /// <summary>按服务器任务及稳定内容聚合；不同任务不会共用同一个提交身份。</summary>
        internal readonly record struct PendingKey(ulong Task, TileChangeKind Kind, StableContentKey Content, int Style);
        /// <summary>记录聚合数量与最后实际活动时间，队列不会无限保留。</summary>
        internal readonly record struct PendingValue(long Count, ulong Tick);
    }
}
