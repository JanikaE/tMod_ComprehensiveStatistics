using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace ComprehensiveStatistics.Common.Networking
{
    /// <summary>合成请求只匹配实际交付所需的物品标量，不传输个人统计增量。</summary>
    internal readonly record struct MagicStorageStack(int Type, int Units, int Prefix, string Content = null, string PrefixContent = null);

    /// <summary>定义独立会话协议和稳定内容名请求指纹，数值 ID 仅供无内容解析器的隔离夹具使用。</summary>
    internal static class MagicStorageConfirmation
    {
        internal const ushort Version = 1;
        internal const byte Standard = 1, Recursive = 2, All = 3;
        internal const int MaxFrame = 128, MaxStacks = 16384, MaxEntries = 4096, MaxPending = 128, MaxPendingEntries = 16384;
        internal const ulong Timeout = 1800;

        /// <summary>按位置、列表顺序、类型、数量和前缀计算请求摘要，解析后的服务器请求必须完全匹配。</summary>
        internal static byte[] Fingerprint(int x, int y, IReadOnlyList<MagicStorageStack> withdrawn, IReadOnlyList<MagicStorageStack> results)
        {
            if (withdrawn == null || results == null || results.Count == 0 || results.Count > MaxStacks || withdrawn.Count > MaxStacks - results.Count) return null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> scalar = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(scalar, x);
            BinaryPrimitives.WriteInt32LittleEndian(scalar[4..], y);
            BinaryPrimitives.WriteInt32LittleEndian(scalar[8..], withdrawn.Count);
            BinaryPrimitives.WriteInt32LittleEndian(scalar[12..], results.Count);
            hash.AppendData(scalar);
            foreach (var collection in new[] { withdrawn, results })
                foreach (var item in collection)
                {
                    if (item.Type <= 0 || item.Units <= 0 || item.Prefix < 0) return null;
                    if (!AppendIdentity(hash, item.Content ?? "#" + item.Type) || !AppendIdentity(hash, item.PrefixContent ?? "#" + item.Prefix)) return null;
                    BinaryPrimitives.WriteInt32LittleEndian(scalar, item.Units);
                    hash.AppendData(scalar[..4]);
                }
            return hash.GetHashAndReset();
        }

        /// <summary>摘要使用稳定内容名而非两端可能不同的运行时 ID；长度前缀避免拼接歧义。</summary>
        private static bool AppendIdentity(IncrementalHash hash, string value)
        {
            int length = Encoding.UTF8.GetByteCount(value);
            if (length < 1 || length > 1025) return false;
            Span<byte> size = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(size, length);
            hash.AppendData(size); hash.AppendData(Encoding.UTF8.GetBytes(value)); return true;
        }

        /// <summary>共用网络流只按显式长度提取当前正文，拒绝截断和超限帧。</summary>
        internal static byte[] ReadFrame(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length < 1 || length > MaxFrame) throw new InvalidDataException();
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
    }

    /// <summary>客户端仅保存自己采集的不可变数量候选；本地流程完成和服务器成功确认须同时成立。</summary>
    internal sealed class MagicStorageConfirmClient
    {
        private readonly Dictionary<ulong, Pending> pending = new();
        private int entries;
        private ulong sequence;
        internal Guid Client { get; private set; }
        internal Guid Server { get; private set; }
        internal byte Capabilities { get; private set; }
        internal int Count => pending.Count;

        /// <summary>新角色或新世界使用新挑战，废弃旧确认及未完成候选。</summary>
        internal void Reset(Guid client) { Client = client; Server = Guid.Empty; Capabilities = 0; sequence = 0; pending.Clear(); entries = 0; }

        /// <summary>仅接受本次挑战的服务器身份和合法共同能力，重复欢迎不重置正在等待的操作。</summary>
        internal bool Welcome(Guid client, Guid server, byte capabilities)
        {
            if (Client == Guid.Empty || client != Client || server == Guid.Empty || (capabilities & ~MagicStorageConfirmation.All) != 0
                || Server != Guid.Empty && Server != server) return false;
            if (Server != Guid.Empty && Capabilities != capabilities) return false;
            Server = server; Capabilities = capabilities; return true;
        }

        /// <summary>发送请求前复制有界候选，序号只在成功登记时推进。</summary>
        internal ulong Begin(byte kind, Dictionary<int, long> totals, ulong tick)
        {
            if ((kind != 1 && kind != 2) || (Capabilities & kind) == 0 || sequence == ulong.MaxValue
                || pending.Count >= MagicStorageConfirmation.MaxPending || totals == null || totals.Count == 0
                || totals.Count > MagicStorageConfirmation.MaxEntries || entries + totals.Count > MagicStorageConfirmation.MaxPendingEntries) return 0;
            foreach (var output in totals) if (output.Key <= 0 || output.Value <= 0) return 0;
            ulong id = ++sequence;
            pending.Add(id, new Pending(new Dictionary<int, long>(totals), tick)); entries += totals.Count;
            return id;
        }

        /// <summary>标记本地调用正常完成，兼容确认在本地结束前到达的情况。</summary>
        internal Dictionary<int, long> Complete(ulong id, ulong tick)
        {
            if (!pending.TryGetValue(id, out Pending value)) return null;
            if (tick - value.Tick > MagicStorageConfirmation.Timeout) { Cancel(id); return null; }
            value.Complete = true;
            return Resolve(id, value);
        }

        /// <summary>仅将对应会话的一次成功确认交给调用者，失败和重放均不增加统计。</summary>
        internal Dictionary<int, long> Acknowledge(Guid client, Guid server, ulong id, bool success, ulong tick)
        {
            if (client != Client || server != Server || server == Guid.Empty || !pending.TryGetValue(id, out Pending value)) return null;
            if (tick - value.Tick > MagicStorageConfirmation.Timeout) { Cancel(id); return null; }
            if (value.Success.HasValue) return null;
            value.Success = success;
            return Resolve(id, value);
        }

        /// <summary>只有双条件成立才移出候选并返回，失败确认可立即丢弃。</summary>
        private Dictionary<int, long> Resolve(ulong id, Pending value)
        {
            if (value.Success == false) { Cancel(id); return null; }
            if (!value.Complete || value.Success != true) return null;
            Cancel(id); return value.Totals;
        }

        /// <summary>异常或超时不保留未确认数量。</summary>
        internal void Cancel(ulong id) { if (pending.Remove(id, out Pending value)) entries -= value.Totals.Count; }

        /// <summary>每帧清理过期候选，等待队列不跨会话补记。</summary>
        internal void Expire(ulong tick)
        {
            var expired = new List<ulong>();
            foreach (var entry in pending) if (tick - entry.Value.Tick > MagicStorageConfirmation.Timeout) expired.Add(entry.Key);
            foreach (ulong id in expired) Cancel(id);
        }

        /// <summary>一次本地候选和两端完成状态。</summary>
        private sealed class Pending
        {
            /// <summary>固定数量和登记时刻。</summary>
            internal Pending(Dictionary<int, long> totals, ulong tick) { Totals = totals; Tick = tick; }
            internal readonly Dictionary<int, long> Totals;
            internal readonly ulong Tick;
            internal bool Complete;
            internal bool? Success;
        }
    }

    /// <summary>服务器每连接只允许一个紧邻上游请求的标记，不接收合成统计增量。</summary>
    internal sealed class MagicStorageConfirmServer
    {
        internal readonly Guid Client, Server = Guid.NewGuid();
        internal readonly byte Capabilities;
        internal bool Ready;
        private ulong lastSequence;
        private Window pending;

        /// <summary>绑定新挑战与协商结果。</summary>
        internal MagicStorageConfirmServer(Guid client, byte capabilities) { Client = client; Capabilities = capabilities; }

        /// <summary>连续且能力合法的标记才能占据下一个实际合成请求。</summary>
        internal bool Begin(ulong id, byte kind, byte[] fingerprint, ulong tick)
        {
            if (pending != null && tick - pending.Tick > MagicStorageConfirmation.Timeout) pending = null;
            if (!Ready || id == 0 || lastSequence == ulong.MaxValue || id != lastSequence + 1
                || (kind != 1 && kind != 2) || (Capabilities & kind) == 0 || fingerprint?.Length != 32) return false;
            lastSequence = id;
            if (pending != null) { pending = null; return false; }
            pending = new Window(id, kind, (byte[])fingerprint.Clone(), tick); return true;
        }

        /// <summary>任何实际请求最多消费一次标记，即使该请求最终失败或指纹不符。</summary>
        internal Window Take(ulong tick)
        {
            Window value = pending; pending = null;
            return value != null && tick - value.Tick <= MagicStorageConfirmation.Timeout ? value : null;
        }

        /// <summary>非法正文和会话失效时清除未使用标记。</summary>
        internal void Reject() => pending = null;

        /// <summary>只撤销同一操作尚未消费的标记，不影响后续序号。</summary>
        internal void Cancel(ulong id) { if (pending?.Id == id) pending = null; }

        /// <summary>一个请求的能力、序号与不可变指纹。</summary>
        internal sealed class Window
        {
            /// <summary>固定服务器收到的请求标记。</summary>
            internal Window(ulong id, byte kind, byte[] hash, ulong tick) { Id = id; Kind = kind; Hash = hash; Tick = tick; }
            internal readonly ulong Id, Tick;
            internal readonly byte Kind;
            private readonly byte[] Hash;
            /// <summary>比较服务器实际解析后的请求，摘要不符时不确认。</summary>
            internal bool Matches(byte[] hash) => hash?.Length == 32 && CryptographicOperations.FixedTimeEquals(Hash, hash);
        }
    }
}
