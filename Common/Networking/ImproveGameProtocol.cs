using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ComprehensiveStatistics.Common.Statistics;

namespace ComprehensiveStatistics.Common.Networking
{
    /// <summary>以稳定内容键回传一个聚合结果，不携带运行时内容编号或任意指标名。</summary>
    internal readonly record struct ImproveGameCredit(TileChangeKind Kind, StableContentKey Content, int Style, long Count);

    /// <summary>定义有界协议与原子解析；包必须完全合法后才更新去重游标及角色数据。</summary>
    internal static class ImproveGameProtocol
    {
        internal const ushort Version = 3;
        internal const int MaxEntries = 24;
        internal const int MaxBodyBytes = 32768;
        internal const int MaxPending = 4096;
        internal const long MaxCount = 1_000_000;
        internal const ImproveGameCapability All = ImproveGameCapability.Space | ImproveGameCapability.BurstSelector
            | ImproveGameCapability.WallSelector | ImproveGameCapability.Vein | ClientCapabilities
            | ImproveGameCapability.Liquid | ImproveGameCapability.NativeLiquid;
        internal const ImproveGameCapability ClientCapabilities = ImproveGameCapability.Construct | ImproveGameCapability.Building
            | ImproveGameCapability.FixedBurst | ImproveGameCapability.Robot;

        /// <summary>读取完整随机身份；截断包不能产生默认身份。</summary>
        internal static Guid ReadIdentity(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            Guid value = new Guid(bytes);
            if (value == Guid.Empty) throw new InvalidDataException("Empty compatibility identity.");
            return value;
        }

        /// <summary>写入当前协议及双方会话身份。</summary>
        internal static void WriteSession(BinaryWriter writer, Guid client, Guid server)
        { writer.Write(Version); writer.Write(client.ToByteArray()); writer.Write(server.ToByteArray()); }

        /// <summary>验证完整会话头；旧协议包不会继续解释正文。</summary>
        internal static bool ReadSession(BinaryReader reader, Guid client, Guid server)
            => reader.ReadUInt16() == Version && ReadIdentity(reader) == client && ReadIdentity(reader) == server;

        /// <summary>写入一批服务器确认结果，批次序号在整个连接会话内单调增长。</summary>
        internal static void WriteBatch(BinaryWriter writer, Guid client, Guid server, ulong task, ulong sequence,
            IReadOnlyList<ImproveGameCredit> entries)
        {
            if (task == 0 || sequence == 0 || entries.Count < 1 || entries.Count > MaxEntries)
                throw new InvalidDataException("Invalid compatibility batch.");
            WriteSession(writer, client, server);
            writer.Write(task); writer.Write(sequence); writer.Write((ushort)entries.Count);
            foreach (ImproveGameCredit entry in entries)
            {
                if (!IsValid(entry)) throw new InvalidDataException("Invalid compatibility credit.");
                writer.Write((byte)entry.Kind); writer.Write(entry.Content.ModName); writer.Write(entry.Content.ContentName);
                writer.Write(entry.Style); writer.Write(entry.Count);
            }
        }

        /// <summary>完整读取并验证整包，防止前半包已入账而后半包非法。</summary>
        internal static bool TryReadBatch(BinaryReader reader, Guid client, Guid server,
            out ulong sequence, out List<ImproveGameCredit> entries)
        {
            entries = null; sequence = 0;
            try
            {
                if (!ReadSession(reader, client, server) || reader.ReadUInt64() == 0) return false;
                sequence = reader.ReadUInt64();
                int count = reader.ReadUInt16();
                if (sequence == 0 || count < 1 || count > MaxEntries) return false;
                var result = new List<ImproveGameCredit>(count);
                for (int i = 0; i < count; i++)
                {
                    TileChangeKind kind = (TileChangeKind)reader.ReadByte();
                    string mod = ReadName(reader), name = ReadName(reader);
                    var entry = new ImproveGameCredit(kind, new StableContentKey(mod, name), reader.ReadInt32(), reader.ReadInt64());
                    if (!IsValid(entry)) return false;
                    result.Add(entry);
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) return false;
                entries = result;
                return true;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is FormatException)
            { entries = null; return false; }
        }

        /// <summary>按结果种类校验白名单、样式及正数量；液体数量是原始单位，不能按格解释。</summary>
        internal static bool IsValid(ImproveGameCredit value)
        {
            if (value.Kind < TileChangeKind.TilePlaced || value.Kind > TileChangeKind.LiquidRemoved
                || !ValidName(value.Content.ModName) || !ValidName(value.Content.ContentName)
                || value.Count <= 0 || value.Count > MaxCount) return false;
            if (value.Kind is TileChangeKind.LiquidPlaced or TileChangeKind.LiquidRemoved)
                return value.Style == 0 && TryGetLiquid(value.Content, out _);
            if (value.Kind is TileChangeKind.WiringPlaced or TileChangeKind.WiringRemoved)
                return value.Style == 0 && TryGetWiringComponent(value.Content, out _);
            return value.Kind is TileChangeKind.ObjectPlaced or TileChangeKind.ObjectRemoved
                ? value.Style >= 0 && value.Style <= ushort.MaxValue : value.Style == 0;
        }

        /// <summary>只接收四种标准液体稳定键，不接受服务器指定任意统计或未知液体。</summary>
        internal static bool TryGetLiquid(StableContentKey content, out LiquidKind liquid)
        {
            int index = content.ModName == "Terraria" ? content.ContentName switch
            { "LiquidWater" => 0, "LiquidLava" => 1, "LiquidHoney" => 2, "LiquidShimmer" => 3, _ => -1 } : -1;
            liquid = (LiquidKind)index;
            return index >= 0;
        }

        /// <summary>只接受既有五种稳定布线组件键，不让网络端创建任意组件。</summary>
        internal static bool TryGetWiringComponent(StableContentKey content, out WiringComponentKind component)
        {
            int index = content.ModName == "Terraria" ? content.ContentName switch
            { "WireRed" => 0, "WireBlue" => 1, "WireGreen" => 2, "WireYellow" => 3, "Actuator" => 4, _ => -1 } : -1;
            component = (WiringComponentKind)index; return index >= 0;
        }

        /// <summary>有界读取 UTF-8，先检查字节长度，避免先分配任意大的字符串。</summary>
        private static string ReadName(BinaryReader reader)
        {
            int length = reader.Read7BitEncodedInt();
            if (length < 1 || length > 512) throw new InvalidDataException("Invalid content name length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        /// <summary>排除空白、控制字符和分隔符；内容能否解析由客户端加载表再验证。</summary>
        private static bool ValidName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 512) return false;
            foreach (char c in value) if (char.IsControl(c) || c == '/' || c == '\\') return false;
            return true;
        }
    }

    /// <summary>维护客户端的确认游标；只接受同一握手会话中连续的新批次。</summary>
    internal sealed class ImproveGameCreditCursor
    {
        internal Guid Client { get; private set; }
        internal Guid Server { get; private set; }
        internal ulong Sequence { get; private set; }
        internal ImproveGameCapability Capabilities { get; private set; }

        /// <summary>进入世界时更换角色连接挑战，退出后拒绝旧欢迎包与旧结果。</summary>
        internal void Reset(Guid client) { Client = client; Server = Guid.Empty; Sequence = 0; Capabilities = 0; }

        /// <summary>重复欢迎包保留游标；同一客户端挑战不允许被另一个服务器身份替换。</summary>
        internal bool Welcome(Guid client, Guid server, ImproveGameCapability capabilities)
        {
            if (Client == Guid.Empty || client != Client || server == Guid.Empty || (capabilities & ~ImproveGameProtocol.All) != 0
                || (Server != Guid.Empty && Server != server)) return false;
            Server = server; Capabilities = capabilities; return true;
        }

        /// <summary>整包及内容验证通过后推进一次；重放、缺口和乱序不记账。</summary>
        internal bool Advance(ulong sequence)
        {
            if (Server == Guid.Empty || Sequence == ulong.MaxValue || sequence != Sequence + 1) return false;
            Sequence = sequence; return true;
        }
    }
}
