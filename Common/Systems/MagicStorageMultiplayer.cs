using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Players;
using ComprehensiveStatistics.Common.Statistics;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>将客户端候选绑定到服务器实际收到并成功处理的魔法存储请求，仅回传成功确认。</summary>
    internal sealed partial class MagicStorageCraftingSystem
    {
        private readonly List<IDisposable> networkHooks = new();
        private static readonly MagicStorageConfirmClient client = new();
        private static readonly Dictionary<int, Peer> peers = new();
        private static readonly object sending = new();
        private static byte installedCapabilities;
        private static bool networkInstalled, supported;
        private static string upstreamVersion;
        private static ulong lastHello;
        private static int helloAttempts;
        [ThreadStatic] private static int? inboundPlayer;
        [ThreadStatic] private static Incoming incoming;

        public override void Load() => On_MessageBuffer.GetData += HandleNetworkMessage;

        public override void PostUpdatePlayers()
        {
            ulong tick = Main.GameUpdateCount;
            if (Main.netMode == NetmodeID.Server)
            {
                foreach (int player in peers.Keys.ToArray()) if (!IsPlayer(peers[player].Player)) peers.Remove(player);
                return;
            }
            client.Expire(tick);
            if (Main.netMode != NetmodeID.MultiplayerClient || Main.gameMenu || Main.LocalPlayer?.active != true) return;
            if (client.Client == Guid.Empty) EnterClientWorld();
            if (client.Server != Guid.Empty || !networkInstalled || helloAttempts >= 10
                || helloAttempts != 0 && tick - lastHello < 180) return;
            lastHello = tick; helloAttempts++;
            SendConfirmation(StatisticsPacketType.MagicStorageHello, packet =>
            { packet.Write(client.Client.ToByteArray()); packet.Write(installedCapabilities); });
        }

        /// <summary>重建当前角色挑战，不允许旧世界或旧角色确认落入当前切片。</summary>
        internal static void EnterClientWorld()
        {
            client.Reset(Main.netMode == NetmodeID.MultiplayerClient ? Guid.NewGuid() : Guid.Empty);
            lastHello = 0; helloAttempts = 0;
        }

        /// <summary>立即移除断线玩家的服务器窗口。</summary>
        internal static void Disconnect(int player) => peers.Remove(player);

        /// <summary>先安装实际结算叶子，再安装发送及接收根入口，最后重建调用方。</summary>
        internal static void InstallNetworkHooks(Assembly code, List<IDisposable> installed)
        {
            Type net = code.GetType("MagicStorage.NetHelper", true);
            Type gui = code.GetType("MagicStorage.CraftingGUI", true);
            Type heart = code.GetType("MagicStorage.Components.TEStorageHeart", true);
            installed.Add(new Hook(Require(gui, "HandleCraftWithdrawAndDeposit", typeof(List<Item>), heart, typeof(List<Item>), typeof(List<Item>)),
                (Func<Func<object, List<Item>, List<Item>, List<Item>>, object, List<Item>, List<Item>, List<Item>>)HandleServerSettlement));
            installed.Add(new Hook(Require(net, "SendCraftRequest", typeof(void), typeof(Point16), typeof(List<Item>), typeof(List<Item>)),
                (Action<Action<Point16, List<Item>, List<Item>>, Point16, List<Item>, List<Item>>)HandleSendRequest));
            installed.Add(new Hook(Require(net, "ReceiveCraftRequest", typeof(void), typeof(BinaryReader), typeof(int)),
                (Action<Action<BinaryReader, int>, BinaryReader, int>)HandleReceiveRequest));
            installed.Add(new ILHook(Require(gui, "Craft", typeof(void), typeof(int)), RecompileCaller));
        }

        /// <summary>固定真实入站连接，不凭本地玩家或上游可伪造参数判断远程归属。</summary>
        private static void HandleNetworkMessage(On_MessageBuffer.orig_GetData orig, MessageBuffer self, int start, int length, out int type)
        {
            int? previous = inboundPlayer;
            inboundPlayer = Main.netMode == NetmodeID.Server && self != null && self.whoAmI >= 0
                && self.whoAmI < Main.player.Length && IsPlayer(Main.player[self.whoAmI]) ? self.whoAmI : null;
            try { orig(self, start, length, out type); }
            finally { inboundPlayer = previous; }
        }

        /// <summary>标记与原请求在同一发送锁内相邻发送；不能采集时仍照常执行上游合成。</summary>
        private static void HandleSendRequest(Action<Point16, List<Item>, List<Item>> orig, Point16 heart, List<Item> withdrawn, List<Item> results)
        {
            lock (sending)
            {
                Operation operation = active;
                if (Main.netMode == NetmodeID.MultiplayerClient && networkInstalled && operation != null
                    && operation.Pending == 0 && MatchesOperation(operation, withdrawn, results))
                {
                    byte[] hash = RequestFingerprint(heart, withdrawn, results);
                    if (hash != null)
                    {
                        byte kind = operation.Recursive ? MagicStorageConfirmation.Recursive : MagicStorageConfirmation.Standard;
                        ulong id = client.Begin(kind, GetTotals(operation), Main.GameUpdateCount);
                        operation.Pending = id;
                        if (id != 0) SendConfirmation(StatisticsPacketType.MagicStorageBegin, packet =>
                        { WriteIdentity(packet, client.Client, client.Server); packet.Write(id); packet.Write(kind); packet.Write(hash); });
                    }
                }
                orig(heart, withdrawn, results);
            }
        }

        /// <summary>实际请求最多消费一个标记，整个接收过程正常结束后才发成功确认。</summary>
        private static void HandleReceiveRequest(Action<BinaryReader, int> orig, BinaryReader reader, int sender)
        {
            Incoming operation = null;
            if (Main.netMode == NetmodeID.Server && inboundPlayer == sender && peers.TryGetValue(sender, out Peer peer)
                && peer.Session.Ready && IsPlayer(peer.Player))
            {
                var window = peer.Session.Take(Main.GameUpdateCount);
                if (window != null) operation = new Incoming(peer, window);
            }
            ExecuteIncomingRequest(() => orig(reader, sender), operation, success =>
            {
                if (peers.TryGetValue(sender, out Peer current) && current == operation.Peer && IsPlayer(current.Player))
                    SendConfirmation(StatisticsPacketType.MagicStorageAck, packet =>
                    { WriteIdentity(packet, current.Session.Client, current.Session.Server); packet.Write(operation.Window.Id); packet.Write((byte)(success ? 1 : 0)); }, sender);
            });
        }

        /// <summary>接收事务的成功条件只由实际结算边界和完整调用结果决定，嵌套请求隔离。</summary>
        private static void ExecuteIncomingRequest(Action orig, Incoming operation, Action<bool> confirm)
        {
            Incoming previous = incoming;
            incoming = operation;
            bool completed = false;
            try { orig(); completed = true; }
            finally
            {
                incoming = previous;
                if (operation != null) confirm(completed && operation.Accepted && operation.Returned);
            }
        }

        /// <summary>对服务器实际解析且即将处理的列表计算指纹，保留对象身份供成功边界验证。</summary>
        private static List<Item> HandleServerSettlement(Func<object, List<Item>, List<Item>, List<Item>> orig, object heart, List<Item> withdrawn, List<Item> results)
        {
            Incoming operation = incoming;
            bool matching = operation != null && !operation.Observed && heart is TileEntity entity
                && operation.Window.Matches(RequestFingerprint(entity.Position, withdrawn, results));
            if (operation != null)
            {
                operation.Observed = true;
                if (matching) { operation.Withdrawn = withdrawn; operation.Results = results; }
            }
            List<Item> leftovers = orig(heart, withdrawn, results);
            if (matching) operation.Returned = true;
            return leftovers;
        }

        /// <summary>只有匹配请求本身经过材料成功边界才可确认，不把失败回退当作产物。</summary>
        private static void MarkServerAccepted(List<Item> withdrawn, List<Item> results)
        {
            if (incoming != null && ReferenceEquals(incoming.Withdrawn, withdrawn) && ReferenceEquals(incoming.Results, results)) incoming.Accepted = true;
        }

        /// <summary>请求摘要只使用解析后物品标量，服务器不接收客户端合成数量或递归计划。</summary>
        private static byte[] RequestFingerprint(Point16 heart, List<Item> withdrawn, List<Item> results)
        {
            if (withdrawn == null || results == null || results.Count > MagicStorageConfirmation.MaxStacks
                || withdrawn.Count > MagicStorageConfirmation.MaxStacks - results.Count) return null;
            MagicStorageStack[] Convert(List<Item> items) => items.Select(item =>
            {
                if (item == null || !StableContentKey.TryCreateForItem(item.type, out StableContentKey content)) return default;
                ModPrefix prefix = PrefixLoader.GetPrefix(item.prefix);
                return new MagicStorageStack(item.type, item.stack, item.prefix, content.ToString(), prefix?.FullName ?? "Terraria/" + item.prefix);
            }).ToArray();
            return MagicStorageConfirmation.Fingerprint(heart.X, heart.Y, Convert(withdrawn), Convert(results));
        }

        /// <summary>完整解析有界正文后才修改会话，错误正文不推进序号也不增加个人统计。</summary>
        internal static void HandleConfirmationPacket(StatisticsPacketType type, BinaryReader reader, int sender)
        {
            try
            {
                byte[] bytes = MagicStorageConfirmation.ReadFrame(reader);
                using var stream = new MemoryStream(bytes, false);
                using var packet = new BinaryReader(stream);
                if (packet.ReadUInt16() != MagicStorageConfirmation.Version) throw new InvalidDataException();
                Guid challenge = ReadIdentity(packet);
                if (Main.netMode == NetmodeID.Server && inboundPlayer == sender && sender >= 0 && sender < Main.player.Length && IsPlayer(Main.player[sender]))
                {
                    if (type == StatisticsPacketType.MagicStorageHello)
                    {
                        byte offered = packet.ReadByte(); RequireEnd(packet);
                        if ((offered & ~MagicStorageConfirmation.All) != 0) return;
                        byte negotiated = (byte)(offered & (networkInstalled ? installedCapabilities : 0));
                        if (!peers.TryGetValue(sender, out Peer peer) || peer.Player != Main.player[sender]
                            || peer.Session.Client != challenge || peer.Session.Capabilities != negotiated)
                            peers[sender] = peer = new Peer(Main.player[sender], new MagicStorageConfirmServer(challenge, negotiated));
                        SendConfirmation(StatisticsPacketType.MagicStorageWelcome, response =>
                        { WriteIdentity(response, challenge, peer.Session.Server); response.Write(negotiated); }, sender);
                    }
                    else
                    {
                        Guid server = ReadIdentity(packet);
                        if (!peers.TryGetValue(sender, out Peer peer) || peer.Player != Main.player[sender]
                            || peer.Session.Client != challenge || peer.Session.Server != server) return;
                        if (type == StatisticsPacketType.MagicStorageReady) { RequireEnd(packet); peer.Session.Ready = true; }
                        else if (type == StatisticsPacketType.MagicStorageBegin)
                        {
                            ulong id = packet.ReadUInt64(); byte kind = packet.ReadByte(); byte[] hash = packet.ReadBytes(32);
                            RequireEnd(packet);
                            if (!peer.Session.Begin(id, kind, hash, Main.GameUpdateCount)) peer.Session.Reject();
                        }
                        else if (type == StatisticsPacketType.MagicStorageAbort)
                        { ulong id = packet.ReadUInt64(); RequireEnd(packet); peer.Session.Cancel(id); }
                    }
                }
                else if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    Guid server = ReadIdentity(packet);
                    if (type == StatisticsPacketType.MagicStorageWelcome)
                    {
                        byte capabilities = packet.ReadByte(); RequireEnd(packet);
                        if ((capabilities & ~installedCapabilities) != 0 || !client.Welcome(challenge, server, capabilities)) return;
                        SendConfirmation(StatisticsPacketType.MagicStorageReady, response => WriteIdentity(response, challenge, server));
                    }
                    else if (type == StatisticsPacketType.MagicStorageAck)
                    {
                        ulong id = packet.ReadUInt64(); byte success = packet.ReadByte(); RequireEnd(packet);
                        if (success > 1) return;
                        RecordConfirmed(client.Acknowledge(challenge, server, id, success == 1, Main.GameUpdateCount));
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is FormatException)
            { if (Main.netMode == NetmodeID.Server && peers.TryGetValue(sender, out Peer peer)) peer.Session.Reject(); }
        }

        /// <summary>确认只写入本次挑战所属的本地角色，统计键仍由既有双范围入口生成。</summary>
        private static void RecordConfirmed(Dictionary<int, long> totals)
        {
            if (totals == null || Main.netMode != NetmodeID.MultiplayerClient || Main.LocalPlayer?.active != true) return;
            foreach (var output in totals) Main.LocalPlayer.GetModPlayer<StatisticsPlayer>().RecordItemCraft(output.Key, output.Value);
        }

        /// <summary>客户端流程异常时撤销候选及紧邻请求标记，服务器已完成的原操作不受修改。</summary>
        private static void AbortPending(ulong id)
        {
            client.Cancel(id);
            if (Main.netMode == NetmodeID.MultiplayerClient && client.Server != Guid.Empty)
                try { SendConfirmation(StatisticsPacketType.MagicStorageAbort, writer => { WriteIdentity(writer, client.Client, client.Server); writer.Write(id); }); }
                catch (Exception error) when (error is IOException || error is InvalidOperationException || error is System.Net.Sockets.SocketException)
                { /* 连接已关闭时由会话失效或超时清理服务器标记，不能覆盖原合成异常。 */ }
        }

        /// <summary>独立包正文始终有版本和显式长度，不借用上游包编号。</summary>
        private static void SendConfirmation(StatisticsPacketType type, Action<BinaryWriter> write, int to = -1)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) { writer.Write(MagicStorageConfirmation.Version); write(writer); }
            if (stream.Length > MagicStorageConfirmation.MaxFrame) throw new InvalidDataException();
            ModPacket packet = ModContent.GetInstance<global::ComprehensiveStatistics.ComprehensiveStatistics>().GetPacket();
            packet.Write((byte)type); packet.Write((ushort)stream.Length); packet.Write(stream.ToArray()); packet.Send(to);
        }

        /// <summary>读取固定长度且非空的随机会话身份。</summary>
        private static Guid ReadIdentity(BinaryReader reader)
        { byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException(); Guid value = new(bytes); if (value == Guid.Empty) throw new InvalidDataException(); return value; }

        /// <summary>写入角色挑战和服务器会话身份。</summary>
        private static void WriteIdentity(BinaryWriter writer, Guid challenge, Guid server) { writer.Write(challenge.ToByteArray()); writer.Write(server.ToByteArray()); }

        /// <summary>拒绝尾随数据，避免部分解析推进会话。</summary>
        private static void RequireEnd(BinaryReader reader) { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException(); }

        /// <summary>在线玩家引用须与该连接当前实例一致。</summary>
        private static bool IsPlayer(Player player) => player?.active == true && player.whoAmI >= 0 && player.whoAmI < Main.player.Length && Main.player[player.whoAmI] == player;

        /// <summary>世界与内容生命周期统一清除确认状态。</summary>
        private static void ResetConfirmation() { peers.Clear(); client.Reset(Guid.Empty); lastHello = 0; helloAttempts = 0; incoming = null; inboundPlayer = null; }

        /// <summary>网络能力卸载先于采集叶子，避免残留接收入口。</summary>
        private void UnloadNetwork()
        { On_MessageBuffer.GetData -= HandleNetworkMessage; DisposeHooks(networkHooks); installedCapabilities = 0; networkInstalled = supported = false; upstreamVersion = null; ResetConfirmation(); }

        /// <summary>服务器连接身份与不持久化窗口。</summary>
        private sealed class Peer
        {
            /// <summary>固定握手时的真实玩家实例。</summary>
            internal Peer(Player player, MagicStorageConfirmServer session) { Player = player; Session = session; }
            internal readonly Player Player;
            internal readonly MagicStorageConfirmServer Session;
        }

        /// <summary>仅在实际接收调用栈内存活的服务器结算证据。</summary>
        private sealed class Incoming
        {
            /// <summary>固定本次真实接收所消费的标记。</summary>
            internal Incoming(Peer peer, MagicStorageConfirmServer.Window window) { Peer = peer; Window = window; }
            internal readonly Peer Peer;
            internal readonly MagicStorageConfirmServer.Window Window;
            internal List<Item> Withdrawn, Results;
            internal bool Observed, Accepted, Returned;
        }
    }
}
