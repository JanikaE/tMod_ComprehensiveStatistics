using System;
using System.Collections.Generic;
using System.IO;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;

namespace ComprehensiveStatistics
{
    /// <summary>提供隔离测试的模组发包出口。</summary>
    internal sealed class ComprehensiveStatistics : Terraria.ModLoader.Mod { }
}
namespace ComprehensiveStatistics.Common.Networking
{
    /// <summary>数值与生产网络分派一致。</summary>
    internal enum StatisticsPacketType : byte
    { ImproveGameHello=8, ImproveGameWelcome=9, ImproveGameReady=10, ImproveGameCreditBatch=11, ImproveGameClientBegin=12, ImproveGameClientTargets=13, ImproveGameClientCommit=14, NativeLiquidBegin=15, NativeLiquidCommit=16 }
}
namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>模拟内容解析；真实序列化结构仍由生产协议实现。</summary>
    internal readonly record struct StableContentKey(string ModName, string ContentName)
    {
        /// <summary>模拟稳定物块名称。</summary>
        internal static bool TryCreateForTileType(int type, out StableContentKey key)
        { key=new("Terraria","Tile"+type);return type>0; }
        /// <summary>模拟稳定墙名称。</summary>
        internal static bool TryCreateForWallType(int type, out StableContentKey key)
        { key=new("Terraria","Wall"+type);return type>0; }
        /// <summary>只允许已知模拟物块。</summary>
        internal bool TryResolveTileType(out int type) { type=0; return ModName=="Terraria" && ContentName.StartsWith("Tile") && int.TryParse(ContentName[4..],out type); }
        /// <summary>只允许已知模拟墙。</summary>
        internal bool TryResolveWallType(out int type) { type=0; return ModName=="Terraria" && ContentName.StartsWith("Wall") && int.TryParse(ContentName[4..],out type); }
    }
}
namespace ComprehensiveStatistics.Common.Players
{
    internal sealed partial class StatisticsPlayer
    {
        /// <summary>记录经过完整协议去重后的提交，测试不会直接修改生产角色存档。</summary>
        internal void RecordConfirmedConstruction(ImproveGameCredit entry)
            => Records.Add($"verified:{entry.Kind}:{entry.Content.ContentName}:{entry.Style}:{entry.Count}");
    }
}
namespace Terraria.ID
{
    /// <summary>模拟家具开关类型的规范映射。</summary>
    internal static class TileID { internal const int OpenDoor=11,ClosedDoor=10,TrapdoorOpen=387,TrapdoorClosed=386,TallGateOpen=389,TallGateClosed=388; }
}
namespace Terraria
{
    internal static partial class Main
    {
        internal static ulong GameUpdateCount;
        internal static Player LocalPlayer => netMode==2 ? throw new Exception("Server read LocalPlayer.") : player[myPlayer];
    }
    /// <summary>提供真实入站上下文与一个模拟原始网络处理器。</summary>
    internal sealed class MessageBuffer { internal int whoAmI; internal Action Body; internal byte[] readBuffer=new byte[]{82}; }
    /// <summary>按正常 Hook 调用链传递连接编号，原始入站处理只执行一次。</summary>
    internal static class On_MessageBuffer
    {
        internal delegate void orig_GetData(MessageBuffer self,int start,int length,out int type);
        internal delegate void hook_GetData(orig_GetData orig,MessageBuffer self,int start,int length,out int type);
        internal static event hook_GetData GetData;
        /// <summary>在指定真实连接上下文中执行一个包。</summary>
        internal static void Deliver(int sender,Action action,byte[] bytes=null)
        {
            orig_GetData chain=(MessageBuffer m,int s,int l,out int t)=>{t=82;m.Body();};
            if(GetData!=null) foreach(hook_GetData hook in GetData.GetInvocationList())
            { var next=chain;chain=(MessageBuffer m,int s,int l,out int t)=>hook(next,m,s,l,out t); }
            bytes??=new byte[]{82};
            chain(new MessageBuffer{whoAmI=sender,Body=action,readBuffer=bytes},0,bytes.Length,out _);
        }
    }
}
namespace Terraria.ModLoader
{
    /// <summary>捕获生产序列化器写出的真实字节及唯一收件人。</summary>
    internal sealed class ModPacket : BinaryWriter
    {
        internal static readonly List<(int To,byte[] Bytes)> Sent=new();
        /// <summary>创建独立的内存包。</summary>
        internal ModPacket():base(new MemoryStream()) { }
        /// <summary>保存数据但不访问网络或用户存档。</summary>
        internal void Send(int to=-1) => Sent.Add((to,((MemoryStream)BaseStream).ToArray()));
    }
    /// <summary>返回可复用的隔离模组实例。</summary>
    internal static class ModContent
    {
        /// <summary>模拟 tModLoader 单例。</summary>
        internal static T GetInstance<T>() where T:new()=>Holder<T>.Value;
        /// <summary>为每种模拟模组保存一个实例。</summary>
        private static class Holder<T> where T:new() { internal static readonly T Value=new(); }
    }
}
