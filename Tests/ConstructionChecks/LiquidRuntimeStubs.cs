using System;
using System.IO;
using ComprehensiveStatistics.Common.Statistics;

namespace Terraria.ID
{
    /// <summary>原版液体工具编号，用于真实生产白名单与意图验证。</summary>
    internal static class ItemID
    {
        internal const int EmptyBucket=205,WaterBucket=206,LavaBucket=207,HoneyBucket=1128,BottomlessBucket=3031,
            SuperAbsorbantSponge=3032,BottomlessLavaBucket=4820,LavaAbsorbantSponge=4872,BottomlessHoneyBucket=5302,
            HoneyAbsorbantSponge=5303,UltraAbsorbantSponge=5304,BottomlessShimmerBucket=5364;
    }
}
namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>稳定值与生产定义一致；真实键及存档另由 DataFormatChecks 检查。</summary>
    internal enum LiquidKind {Water=0,Lava=1,Honey=2,Shimmer=3}
}
namespace ComprehensiveStatistics.Common.Players
{
    /// <summary>单人液体采集输出夹具。</summary>
    internal sealed partial class StatisticsPlayer
    {
        /// <summary>仅收集本地单人输出；服务器访问出口会由 Player 夹具报错。</summary>
        internal void RecordLiquidChange(LiquidChange change)=>Records.Add($"liquid:{change.PlacedUnits}:{change.RemovedUnits}");
    }
}
namespace Terraria
{
    /// <summary>原版液体工具与动作夹具。</summary>
    internal sealed partial class Item {internal int type=0;internal Action<Player> LiquidAction;}
    /// <summary>驱动生产原版液体事件的玩家夹具。</summary>
    internal sealed partial class Player
    {
        internal static int tileTargetX=4,tileTargetY=4;
        /// <summary>运行生产原版工具事件链，动作本身由场景指定。</summary>
        internal void UseLiquid(Item tool)
        {
            On_Player.orig_ItemCheck_UseBuckets chain=(p,item)=>item.LiquidAction?.Invoke(p);
            if(On_Player.ItemCheck_UseBuckets!=null)foreach(On_Player.hook_ItemCheck_UseBuckets hook in On_Player.ItemCheck_UseBuckets.GetInvocationList())
            {var next=chain;chain=(p,item)=>hook(next,p,item);}
            chain(this,tool);
        }
    }
    /// <summary>原版工具事件签名夹具。</summary>
    internal static class On_Player
    {
        /// <summary>原版桶处理签名。</summary>
        internal delegate void orig_ItemCheck_UseBuckets(Player player,Item item);
        /// <summary>桶处理 Hook 签名。</summary>
        internal delegate void hook_ItemCheck_UseBuckets(orig_ItemCheck_UseBuckets orig,Player player,Item item);
        internal static hook_ItemCheck_UseBuckets ItemCheck_UseBuckets;
    }
    /// <summary>服务器液体反应边界夹具。</summary>
    internal static partial class WorldGen
    {
        internal static Action LiquidFrame;
        /// <summary>在真正接受消息 48 的写入之后执行生产捕获边界。</summary>
        internal static void SquareTileFrame(int x,int y,bool resetFrame=false)
        {
            On_WorldGen.orig_SquareTileFrame chain=(i,j,r)=>LiquidFrame?.Invoke();
            if(On_WorldGen.SquareTileFrame!=null)foreach(On_WorldGen.hook_SquareTileFrame hook in On_WorldGen.SquareTileFrame.GetInvocationList())
            {var next=chain;chain=(i,j,r)=>hook(next,i,j,r);}
            chain(x,y,resetFrame);
        }
    }
    /// <summary>帧修复事件表面。</summary>
    internal static partial class On_WorldGen
    {
        /// <summary>帧修复原函数签名。</summary>
        internal delegate void orig_SquareTileFrame(int x,int y,bool resetFrame);
        /// <summary>帧修复 Hook 签名。</summary>
        internal delegate void hook_SquareTileFrame(orig_SquareTileFrame orig,int x,int y,bool resetFrame);
        internal static hook_SquareTileFrame SquareTileFrame;
    }
    /// <summary>自然更新边界夹具。</summary>
    internal sealed class Liquid { }
    /// <summary>液体更新和入队的事件表面。</summary>
    internal static class On_Liquid
    {
        /// <summary>自然更新原函数签名。</summary>
        internal delegate void orig_Update(Liquid liquid);
        /// <summary>自然更新 Hook 签名。</summary>
        internal delegate void hook_Update(orig_Update orig,Liquid liquid);
        /// <summary>液体入队原函数签名。</summary>
        internal delegate void orig_AddWater(int x,int y);
        /// <summary>液体入队 Hook 签名。</summary>
        internal delegate void hook_AddWater(orig_AddWater orig,int x,int y);
        internal static hook_Update Update;
        internal static hook_AddWater AddWater;
    }
    /// <summary>原版 sendWater HookGen 表面，发送的是原始消息 48 而非统计结果。</summary>
    internal static partial class On_NetMessage
    {
        /// <summary>原版单格液体同步签名。</summary>
        internal delegate void orig_sendWater(int x,int y);
        /// <summary>单格液体同步 Hook 签名。</summary>
        internal delegate void hook_sendWater(orig_sendWater orig,int x,int y);
        internal static hook_sendWater sendWater;
    }
    /// <summary>通过生产发送边界输出原版包的隔离网络夹具。</summary>
    internal static partial class NetMessage
    {
        /// <summary>先触发生产操作标记，再序列化客户端世界状态。</summary>
        internal static void sendWater(int x,int y)
        {
            On_NetMessage.orig_sendWater chain=(i,j)=>
                SendData(48,number:i,number2:j);
            if(On_NetMessage.sendWater!=null)foreach(On_NetMessage.hook_sendWater hook in On_NetMessage.sendWater.GetInvocationList())
            {var next=chain;chain=(i,j)=>hook(next,i,j);}
            chain(x,y);
        }

        /// <summary>所有消息 48 都经过真实生产发送 Hook，直接调用也必须占据真实包序。</summary>
        internal static void SendData(int message,int to=-1,int ignore=-1,Localization.NetworkText text=null,int number=0,
            float number2=0,float number3=0,float number4=0,int number5=0,int number6=0,int number7=0)
        {
            On_NetMessage.orig_SendData chain=(m,t,g,txt,n,n2,n3,n4,n5,n6,n7)=>
            {
                if(m!=48)throw new Exception("unexpected fixture message");
                using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
                int i=n,j=(int)n2;writer.Write((byte)48);writer.Write((short)i);writer.Write((short)j);
                writer.Write((byte)Main.Tiles[i,j].LiquidAmount);writer.Write((byte)Main.Tiles[i,j].LiquidType);
                Terraria.ModLoader.ModPacket.Sent.Add((-1,stream.ToArray()));
            };
            if(On_NetMessage.SendData!=null)foreach(On_NetMessage.hook_SendData hook in On_NetMessage.SendData.GetInvocationList())
            {var next=chain;chain=(m,t,g,txt,n,n2,n3,n4,n5,n6,n7)=>hook(next,m,t,g,txt,n,n2,n3,n4,n5,n6,n7);}
            chain(message,to,ignore,text,number,number2,number3,number4,number5,number6,number7);
        }
    }
}
namespace ImproveGame.Content.Items
{
    /// <summary>实际 Hook 的液体叶子夹具，继承共享选择器，以验证服务器任务身份链。</summary>
    internal class LiquidWand:SelectorItem
    {
        internal bool Absorb=false;
        internal int Kind=0,Units=255;
        /// <summary>按场景直接改变当前格位液体量。</summary>
        public override bool ModifySelectedTiles(Terraria.Player player,int x,int y)
        {var tile=Terraria.Main.Tiles[x,y];tile.LiquidType=Kind;tile.LiquidAmount=Absorb?0:Units;return true;}
    }
    /// <summary>高级形态沿用同一实际叶子。</summary>
    internal sealed class LiquidWandAdvanced:LiquidWand { }
}
