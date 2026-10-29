using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ImproveGame.Content.Items;
using Microsoft.Xna.Framework;
using Terraria;

namespace ImproveGame.Content.Packets
{
    /// <summary>模拟 NetSimplified 提供的 Sender 属性。</summary>
    internal abstract class Packet
    {
        protected int Sender { get; set; }
        /// <summary>仅供测试注入底层网络层设置的发送者。</summary>
        internal int Source { get=>Sender; set=>Sender=value; }
    }
    /// <summary>沿用实际上游网络路径调用空间操作。</summary>
    internal sealed class SpaceWandOperation : Packet
    {
        internal HashSet<Point> Targets=new(){new(4,4)};
        /// <summary>服务器从 Sender 选择玩家，原函数仍由生产 Hook 转发。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Receive()
        {
            bool sound=false;var consumed=new Dictionary<int,int>();
            foreach(var p in Targets) SpaceWand.OperateTile(Main.player[Sender],p.X,p.Y,Targets,PlaceType.Soild,Terraria.ID.BlockType.Solid,ref sound,consumed);
        }
    }
    /// <summary>模拟接收后调度而非立即执行的选择器任务。</summary>
    internal sealed class SelectorItemOperation : Packet
    {
        internal SelectorItem Item=new MagickWand();
        internal Rectangle Area=new(4,4,3,1);
        internal IEnumerator Pending;
        /// <summary>只创建任务，后续逐帧推进时已无入站上下文。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Receive()=>Pending=Item.ModifyTiles(Main.player[Sender],Area);
    }
}
namespace ImproveGame.Packets.Tiles
{
    /// <summary>模拟真实上游只在服务器执行并设置 MinerIndex 的入口。</summary>
    internal sealed class VeinMiningPacket : ImproveGame.Content.Packets.Packet
    {
        /// <summary>上游会设置并清除矿工全局字段，统计不得在入口外使用旧值。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Receive()
        {
            if(Main.netMode==1)return;
            Content.Functions.VeinMiner.VeinMinerSystem.MinerIndex=Sender;
            Content.Functions.VeinMiner.VeinMinerSystem.DoVeinMiningAt(new Point(4,4),1);
            Content.Functions.VeinMiner.VeinMinerSystem.MinerIndex=-1;
        }
    }
}
