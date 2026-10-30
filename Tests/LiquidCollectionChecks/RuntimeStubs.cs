using System;
using System.Collections.Generic;
using System.Reflection;
using ComprehensiveStatistics.Common.Statistics;

namespace Microsoft.Xna.Framework
{
    /// <summary>选区夹具，保留工厂方法参数的类型与语义。</summary>
    internal readonly record struct Rectangle(int X, int Y, int Width, int Height)
    {
        /// <summary>左上闭、右下开的直接目标集合。</summary>
        internal bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    }
}
namespace Terraria.ID
{
    /// <summary>模式值与实际游戏一致。</summary>
    internal static class NetmodeID { internal const int SinglePlayer = 0, Server=2, MultiplayerClient=1; }
    /// <summary>LQ1 实际原版物品类型，避免测试夹具漏掉某类桶或海绵。</summary>
    internal static class ItemID
    {
        internal const int EmptyBucket=205,WaterBucket=206,LavaBucket=207,HoneyBucket=1128,
            BottomlessBucket=3031,SuperAbsorbantSponge=3032,BottomlessLavaBucket=4820,LavaAbsorbantSponge=4872,
            BottomlessHoneyBucket=5302,HoneyAbsorbantSponge=5303,UltraAbsorbantSponge=5304,BottomlessShimmerBucket=5364;
    }
}
namespace Terraria
{
    /// <summary>单人回归只验证事件安装释放，联机同步在 ConstructionChecks 双端测试中执行。</summary>
    internal static partial class On_NetMessage
    {
        internal delegate void orig_sendWater(int x,int y);
        internal delegate void hook_sendWater(orig_sendWater orig,int x,int y);
        internal static hook_sendWater sendWater;
    }
    /// <summary>可变格位用来检测快照是否错误保留了活引用。</summary>
    internal sealed class Tile { internal int LiquidType, LiquidAmount; }
    /// <summary>工具夹具的原函数行为由场景设置。</summary>
    internal sealed class Item { internal int type; internal Action<Player> Execute; }
    /// <summary>仅提供身份和统计出口，不复刻生产采集。</summary>
    internal sealed class Player
    {
        internal int whoAmI;
        internal bool active = true;
        internal object HeldItem;
        internal static int tileTargetX=4,tileTargetY=4;
        internal readonly global::ComprehensiveStatistics.Common.Players.StatisticsPlayer Statistics = new();
        /// <summary>服务器任何访问个人统计都会令夹具失败。</summary>
        internal T GetModPlayer<T>() => Main.netMode == 2 ? throw new Exception("server accessed personal data") : (T)(object)Statistics;
        /// <summary>按原版 HookGen 事件链执行工具原函数。</summary>
        internal void Use(Item item)
        {
            On_Player.orig_ItemCheck_UseBuckets chain=(player,tool)=>tool.Execute?.Invoke(player);
            if(On_Player.ItemCheck_UseBuckets != null)
                foreach(On_Player.hook_ItemCheck_UseBuckets hook in On_Player.ItemCheck_UseBuckets.GetInvocationList())
                { var next=chain; chain=(player,tool)=>hook(next,player,tool); }
            chain(this,item);
        }
    }
    /// <summary>只包含采集器需要的身份与格位。</summary>
    internal static class Main
    {
        internal static int netMode,myPlayer;
        internal static bool gameMenu,dedServ;
        internal static Player[] player={new(){whoAmI=0},new(){whoAmI=1}};
        internal static readonly Tile[,] Tiles=new Tile[16,16];
    }
    /// <summary>原版工具 HookGen 签名夹具。</summary>
    internal static class On_Player
    {
        /// <summary>原函数签名。</summary>
        internal delegate void orig_ItemCheck_UseBuckets(Player player,Item item);
        /// <summary>Hook 签名。</summary>
        internal delegate void hook_ItemCheck_UseBuckets(orig_ItemCheck_UseBuckets orig,Player player,Item item);
        internal static hook_ItemCheck_UseBuckets ItemCheck_UseBuckets;
    }
    /// <summary>格位边界及可注入环境变化。</summary>
    internal static class WorldGen
    {
        internal static Action Frame;
        /// <summary>使用小型隔离世界。</summary>
        internal static bool InWorld(int x,int y)=>x>=0&&y>=0&&x<16&&y<16;
        /// <summary>执行真实生产代码挂接的帧边界。</summary>
        internal static void SquareTileFrame(int x,int y,bool resetFrame=false)
        {
            On_WorldGen.orig_SquareTileFrame chain=(i,j,reset)=>Frame?.Invoke();
            if(On_WorldGen.SquareTileFrame!=null)
                foreach(On_WorldGen.hook_SquareTileFrame hook in On_WorldGen.SquareTileFrame.GetInvocationList())
                {var next=chain;chain=(i,j,reset)=>hook(next,i,j,reset);}
            chain(x,y,resetFrame);
        }
    }
    /// <summary>帧 HookGen 事件夹具。</summary>
    internal static class On_WorldGen
    {
        /// <summary>原函数签名。</summary>
        internal delegate void orig_SquareTileFrame(int x,int y,bool resetFrame);
        /// <summary>Hook 签名。</summary>
        internal delegate void hook_SquareTileFrame(orig_SquareTileFrame orig,int x,int y,bool resetFrame);
        internal static hook_SquareTileFrame SquareTileFrame;
    }
    /// <summary>仅从夹具世界读取可变格位。</summary>
    internal static class Framing
    {
        /// <summary>按坐标读取当前世界。</summary>
        internal static Tile GetTileSafely(int x,int y)=>Main.Tiles[x,y];
    }
    /// <summary>同步自然更新与入队行为夹具，独立于工具修改。</summary>
    internal sealed class Liquid
    {
        internal static Action UpdateBody,AddBody;
        /// <summary>触发实际采集器的自然更新抑制事件。</summary>
        internal void Update()
        {
            On_Liquid.orig_Update chain=liquid=>UpdateBody?.Invoke();
            if(On_Liquid.Update!=null) foreach(On_Liquid.hook_Update hook in On_Liquid.Update.GetInvocationList())
            {var next=chain;chain=liquid=>hook(next,liquid);}
            chain(this);
        }
        /// <summary>触发实际采集器的液体入队抑制事件。</summary>
        internal static void AddWater(int x,int y)
        {
            On_Liquid.orig_AddWater chain=(i,j)=>AddBody?.Invoke();
            if(On_Liquid.AddWater!=null) foreach(On_Liquid.hook_AddWater hook in On_Liquid.AddWater.GetInvocationList())
            {var next=chain;chain=(i,j)=>hook(next,i,j);}
            chain(x,y);
        }
    }
    /// <summary>液体更新 HookGen 签名夹具。</summary>
    internal static class On_Liquid
    {
        /// <summary>原更新函数。</summary>
        internal delegate void orig_Update(Liquid liquid);
        /// <summary>更新 Hook。</summary>
        internal delegate void hook_Update(orig_Update orig,Liquid liquid);
        /// <summary>原入队函数。</summary>
        internal delegate void orig_AddWater(int x,int y);
        /// <summary>入队 Hook。</summary>
        internal delegate void hook_AddWater(orig_AddWater orig,int x,int y);
        internal static hook_Update Update;
        internal static hook_AddWater AddWater;
    }
}
namespace ComprehensiveStatistics.Common.Networking
{
    /// <summary>单人采集回归不模拟握手，联机生产实现另由双端工程直接测试。</summary>
    internal enum ImproveGameCapability { Liquid=256 }
}
namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>隔离单人回归的联机出口，服务器归属与回传使用另一工程的真实生产实现验证。</summary>
    internal static class ImproveGameMultiplayerSystem
    {
        internal static bool HasNativeStep=>false;
        /// <summary>单人不建立服务器任务。</summary>
        internal static Authority Capture(Terraria.Player player,global::ComprehensiveStatistics.Common.Networking.ImproveGameCapability capability)=>null;
        /// <summary>单人工程保留能力发布接口。</summary>
        internal static void PublishLiquid(bool native,bool wand) { }
        /// <summary>单人不创建原版联机窗口。</summary>
        internal static IDisposable BeginNativeStep(Terraria.Player player,int x,int y,int tool)=>null;
        /// <summary>单人不发送原版联机标记。</summary>
        internal static IDisposable BeginNativeSend(int x,int y,bool direct)=>null;
        /// <summary>单人没有服务器入站窗口。</summary>
        internal static void CaptureNativeBeforeFrame(int x,int y) { }
        /// <summary>单人工程只验证 IL 形状，联机工程从实际入站写入取证。</summary>
        internal static void CaptureNativeAfterWrite() { }
        /// <summary>不为单人夹具提供服务器授权。</summary>
        internal sealed class Authority
        {
            /// <summary>单人入口不可伪装服务器权威。</summary>
            internal bool Allows(Terraria.Player player,global::ComprehensiveStatistics.Common.Networking.ImproveGameCapability capability)=>false;
            /// <summary>此出口不应被单人采集调用。</summary>
            internal global::ComprehensiveStatistics.Common.Statistics.LiquidChangeScope BeginLiquid(Terraria.Player player,int x,int y)=>throw new Exception("unexpected server path");
        }
    }
}
namespace Terraria.ModLoader
{
    /// <summary>记录安装日志和异常降级。</summary>
    internal sealed class Logger
    {
        internal static int Warnings;
        /// <summary>输出采集器安装状态。</summary>
        internal void Info(string message)=>Console.WriteLine(message);
        /// <summary>收集异常降级供断言。</summary>
        internal void Warn(string message){Warnings++;Console.WriteLine(message);}
    }
    /// <summary>上游程序集由本测试工程定义，真实 Hook 仍由 MonoMod 实现。</summary>
    internal sealed class Mod { internal Assembly Code=>typeof(Mod).Assembly; internal Version Version=new(1,8,2); internal Logger Logger=new(); }
    /// <summary>仅提供生产系统所需生命周期。</summary>
    internal abstract class ModSystem
    {
        internal Mod Mod=new();
        public virtual void Load() { }
        public virtual void Unload() { }
        public virtual void OnWorldLoad() { }
        public virtual void OnWorldUnload() { }
    }
}
namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>枚举与生产标准液体值一致，键/存档另由 DataFormatChecks 使用真实生产类型验证。</summary>
    internal enum LiquidKind {Water=0,Lava=1,Honey=2,Shimmer=3}
}
namespace ComprehensiveStatistics.Common.Players
{
    /// <summary>只保存生产采集器输出，不复制差量或归属逻辑。</summary>
    internal sealed class StatisticsPlayer
    {
        internal readonly List<LiquidChange> Records=new();
        /// <summary>保留实际生产差量供断言。</summary>
        internal void RecordLiquidChange(LiquidChange change)=>Records.Add(change);
    }
}
