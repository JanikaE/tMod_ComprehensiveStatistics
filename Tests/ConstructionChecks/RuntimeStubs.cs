using System;
using System.Reflection;
namespace Microsoft.Xna.Framework
{
    /// <summary>仅用于工厂签名和测试坐标。</summary>
    internal readonly record struct Point(int X, int Y);
    /// <summary>测试中直接用格位坐标表示鼠标目标，避免引入图形依赖。</summary>
    internal readonly record struct MousePoint(int X, int Y)
    {
        /// <summary>返回模拟的鼠标格位坐标。</summary>
        internal Point ToTileCoordinates() => new(X,Y);
    }
}
namespace Terraria
{
    /// <summary>补充适配器读取的鼠标坐标。</summary>
    internal static partial class Main { internal static Microsoft.Xna.Framework.MousePoint MouseWorld; }
    /// <summary>模拟放置物品参数。</summary>
    internal sealed partial class Item { internal int createTile = 2, createWall = 2; }
    /// <summary>用于验证 out 参数完整传回。</summary>
    internal struct TileObject { internal int Marker; }
    internal static partial class WorldGen
    {
        internal static int CascadeToX = -1, FailX = -1;
        internal static int ServerDrops;
        /// <summary>模拟原版放置，并运行生产验证器的放置前边界。</summary>
        internal static bool PlaceTile(int x,int y,int type,bool mute=false,bool forced=false,int player=-1,int style=0)
        {
            On_WorldGen.orig_PlaceTile chain=(i,j,t,m,f,p,s)=>{Main.Tiles[i,j].HasTile=true;Main.Tiles[i,j].TileType=t;return true;};
            if(On_WorldGen.PlaceTile!=null)foreach(On_WorldGen.hook_PlaceTile hook in On_WorldGen.PlaceTile.GetInvocationList())
            {var next=chain;chain=(i,j,t,m,f,p,s)=>hook(next,i,j,t,m,f,p,s);}
            return chain(x,y,type,mute,forced,player,style);
        }
        /// <summary>模拟原版放墙入口。</summary>
        internal static void PlaceWall(int x,int y,int type,bool mute=false)
        {
            On_WorldGen.orig_PlaceWall chain=(i,j,t,m)=>Main.Tiles[i,j].WallType=t;
            if(On_WorldGen.PlaceWall!=null)foreach(On_WorldGen.hook_PlaceWall hook in On_WorldGen.PlaceWall.GetInvocationList())
            {var next=chain;chain=(i,j,t,m)=>hook(next,i,j,t,m);}
            chain(x,y,type,mute);
        }
        /// <summary>模拟清理区内相邻格被提前联动拆除，以及权限拒绝。</summary>
        internal static void KillTile(int x, int y, bool fail = false, bool effectOnly = false, bool noItem = false)
        {
            On_WorldGen.orig_KillTile original = (i, j, f, effect, noDrop) =>
            {
                if (f || effect || i == FailX || !Main.Tiles[i,j].HasTile) return;
                if(Main.netMode==2)ServerDrops++;
                Main.Tiles[i,j].HasTile=false;
                if (i < CascadeToX) KillTile(i+1,j);
            };
            var chain=original;
            if(On_WorldGen.KillTile!=null) foreach(On_WorldGen.hook_KillTile hook in On_WorldGen.KillTile.GetInvocationList())
            { var next=chain; chain=(i,j,f,e,n)=>hook(next,i,j,f,e,n); }
            chain(x,y,fail,effectOnly,noItem);
        }
        /// <summary>模拟背景墙移除并触发生产适配器安装的事件。</summary>
        internal static void KillWall(int x, int y, bool fail = false)
        {
            On_WorldGen.orig_KillWall chain=(i,j,f)=> { if(!f) Main.Tiles[i,j].WallType=0; };
            if(On_WorldGen.KillWall!=null) foreach(On_WorldGen.hook_KillWall hook in On_WorldGen.KillWall.GetInvocationList())
            { var next=chain; chain=(i,j,f)=>hook(next,i,j,f); }
            chain(x,y,fail);
        }
        /// <summary>模拟组件放置。</summary>
        internal static bool PlaceWire(int x, int y) => On_WorldGen.PlaceWire?.Invoke((i,j) => Main.Tiles[i,j].RedWire=true,x,y) ?? (Main.Tiles[x,y].RedWire=true);
        /// <summary>模拟组件放置。</summary>
        internal static bool PlaceWire2(int x, int y) => On_WorldGen.PlaceWire2?.Invoke((i,j) => Main.Tiles[i,j].BlueWire=true,x,y) ?? (Main.Tiles[x,y].BlueWire=true);
        /// <summary>模拟组件放置。</summary>
        internal static bool PlaceWire3(int x, int y) => On_WorldGen.PlaceWire3?.Invoke((i,j) => Main.Tiles[i,j].GreenWire=true,x,y) ?? (Main.Tiles[x,y].GreenWire=true);
        /// <summary>模拟组件放置。</summary>
        internal static bool PlaceWire4(int x, int y) => On_WorldGen.PlaceWire4?.Invoke((i,j) => Main.Tiles[i,j].YellowWire=true,x,y) ?? (Main.Tiles[x,y].YellowWire=true);
        /// <summary>模拟组件放置。</summary>
        internal static bool PlaceActuator(int x, int y) => On_WorldGen.PlaceActuator?.Invoke((i,j) => Main.Tiles[i,j].HasActuator=true,x,y) ?? (Main.Tiles[x,y].HasActuator=true);
    }
    /// <summary>模拟 HookGen 事件签名，不替代真实 MonoMod 方法 Hook。</summary>
    internal static partial class On_WorldGen
    {
        internal delegate bool orig_PlaceTile(int x,int y,int type,bool mute,bool forced,int player,int style);
        internal delegate bool hook_PlaceTile(orig_PlaceTile orig,int x,int y,int type,bool mute,bool forced,int player,int style);
        internal static hook_PlaceTile PlaceTile;
        internal delegate void orig_PlaceWall(int x,int y,int type,bool mute);
        internal delegate void hook_PlaceWall(orig_PlaceWall orig,int x,int y,int type,bool mute);
        internal static hook_PlaceWall PlaceWall;
        /// <summary>模拟物块破坏原始方法签名。</summary>
        internal delegate void orig_KillTile(int x,int y,bool fail,bool effectOnly,bool noItem);
        /// <summary>模拟物块破坏 Hook 签名。</summary>
        internal delegate void hook_KillTile(orig_KillTile orig,int x,int y,bool fail,bool effectOnly,bool noItem);
        internal static hook_KillTile KillTile;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate void orig_KillWall(int x,int y,bool fail);
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate void hook_KillWall(orig_KillWall orig,int x,int y,bool fail);
        internal static hook_KillWall KillWall;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool orig_PlaceWire(int x,int y);
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool hook_PlaceWire(orig_PlaceWire orig,int x,int y);
        internal static hook_PlaceWire PlaceWire;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool orig_PlaceWire2(int x,int y);
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool hook_PlaceWire2(orig_PlaceWire2 orig,int x,int y);
        internal static hook_PlaceWire2 PlaceWire2;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool orig_PlaceWire3(int x,int y);
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool hook_PlaceWire3(orig_PlaceWire3 orig,int x,int y);
        internal static hook_PlaceWire3 PlaceWire3;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool orig_PlaceWire4(int x,int y);
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool hook_PlaceWire4(orig_PlaceWire4 orig,int x,int y);
        internal static hook_PlaceWire4 PlaceWire4;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool orig_PlaceActuator(int x,int y);
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        internal delegate bool hook_PlaceActuator(orig_PlaceActuator orig,int x,int y);
        internal static hook_PlaceActuator PlaceActuator;
    }
}
namespace Terraria.ID
{
    /// <summary>匹配标准网络模式值。</summary>
}
namespace Terraria.ModLoader.IO
{
    /// <summary>只用于构造工厂参数。</summary>
    internal sealed class TagCompound { }
}
namespace Terraria.ModLoader
{
    /// <summary>保存能力安装结果，任何意外降级均令测试失败。</summary>
    internal sealed class Logger
    {
        internal static int Warnings;
        /// <summary>记录安装信息。</summary>
        internal void Info(string message) => Console.WriteLine(message);
        /// <summary>记录降级错误。</summary>
        internal void Warn(string message) { Warnings++; Console.WriteLine(message); }
    }
    /// <summary>提供模拟上游程序集及可配置版本，以验证最低版本和后续版本。</summary>
    internal class Mod
    {
        internal Assembly Code => typeof(Mod).Assembly;
        internal Version Version = new(1,8,2);
        internal Logger Logger = new();
        /// <summary>创建隔离的待发送包。</summary>
        internal ModPacket GetPacket() => new();
    }
    /// <summary>提供上游发现。</summary>
    internal static class ModLoader
    {
        internal static bool Available=true;
        internal static Mod Upstream=new();
        /// <summary>模拟可选依赖是否安装。</summary>
        internal static bool TryGetMod(string name,out Mod mod) { mod=Upstream; return Available; }
    }
    /// <summary>提供生产适配器所需生命周期。</summary>
    internal abstract class ModSystem
    {
        internal Mod Mod = new();
        public virtual void Load() { }
        public virtual void PostUpdatePlayers() { }
        public virtual void PostUpdateWorld() { }
        public virtual void PostSetupContent() { }
        public virtual void OnWorldLoad() { }
        public virtual void OnWorldUnload() { }
        public virtual void Unload() { }
    }
}

