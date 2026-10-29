using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Terraria;
namespace Microsoft.Xna.Framework
{
    /// <summary>提供选区的值语义。</summary>
    internal readonly record struct Rectangle(int X,int Y,int Width,int Height)
    {
        /// <summary>判断直接目标是否在矩形内。</summary>
        internal bool Contains(int x,int y)=>x>=X&&y>=Y&&x<X+Width&&y<Y+Height;
    }
}
namespace Terraria.ID
{
    /// <summary>模拟坡形参数，不参与内容身份。</summary>
    internal enum BlockType { Solid, Slope }
}
namespace Terraria
{
    /// <summary>保存机器人实例与出生所有者。</summary>
    internal sealed class Projectile { internal int owner; internal Terraria.ModLoader.ModProjectile ModProjectile; }
}
namespace Terraria.ModLoader
{
    /// <summary>提供机器人 AI 的基类。</summary>
    internal abstract class ModProjectile { internal Projectile Projectile=new(); }
}
namespace ImproveGame.Content.Items
{
    /// <summary>保留实际 byte 枚举 ABI。</summary>
    internal enum PlaceType:byte { Platform,Soild }
    /// <summary>模拟空间工具的真实参数与 ref 参数。</summary>
    internal static class SpaceWand
    {
        internal static bool Fail,RebuildSame,ThrowAfterPlace;
        /// <summary>实际修改指定格位或只改坡形。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void OperateTile(Player player,int x,int y,HashSet<Point> selected,PlaceType place,Terraria.ID.BlockType block,
            ref bool sound,Dictionary<int,int> consumed)
        {
            if(Fail||!selected.Contains(new Point(x,y)))return;
            if(block==Terraria.ID.BlockType.Slope){Main.Tiles[x,y].Slope=1;return;}
            int type=RebuildSame?Main.Tiles[x,y].TileType:2;
            if(Main.Tiles[x,y].HasTile) WorldGen.KillTile(x,y);
            Main.Tiles[x,y].HasTile=true;Main.Tiles[x,y].TileType=type;
            if(ThrowAfterPlace)throw new InvalidOperationException("injected space failure");
            sound=true;consumed[2]=1;
        }
    }
    /// <summary>模拟实际选择器跨帧循环。</summary>
    internal class SelectorItem
    {
        /// <summary>逐格执行并在每格后让出。</summary>
        internal IEnumerator ModifyTiles(Player player,Rectangle rectangle)
        {
            for(int x=rectangle.X;x<rectangle.X+rectangle.Width;x++)
            for(int y=rectangle.Y;y<rectangle.Y+rectangle.Height;y++)
            {if(!ModifySelectedTiles(player,x,y))yield break;yield return null;}
        }
        /// <summary>未适配选择器的默认入口。</summary>
        public virtual bool ModifySelectedTiles(Player player,int x,int y)=>true;
    }
    /// <summary>模拟法爆的固定与框选入口。</summary>
    internal class MagickWand:SelectorItem
    {
        internal bool Fixed=true,TileMode=true,WallMode=true;
        /// <summary>获得固定选区。</summary>
        protected Rectangle GetRectangle(Player player)=>new(4,4,3,1);
        /// <summary>固定模式直接修改，避免用框选路径替代测试。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void FixedModeAction(Player player)
        {if(!Fixed)return;for(int x=4;x<7;x++){if(TileMode)WorldGen.KillTile(x,4);if(WallMode)WorldGen.KillWall(x,4);}}
        /// <summary>调用固定模式入口。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void UseFixed(Player player)=>FixedModeAction(player);
        /// <summary>框选入口仅拆除开启的内容层。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override bool ModifySelectedTiles(Player player,int x,int y)
        {if(TileMode)WorldGen.KillTile(x,y);if(WallMode)WorldGen.KillWall(x,y);return true;}
    }
    /// <summary>星爆继承法爆行为。</summary>
    internal sealed class StarburstWand:MagickWand { }
    /// <summary>科技框选分两次执行拆墙和放墙。</summary>
    internal sealed class WallPlaceSelectorMode:SelectorItem
    {
        /// <summary>旧墙存在时只拆墙，否则放新墙。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override bool ModifySelectedTiles(Player player,int x,int y)
        {if(Main.Tiles[x,y].WallType==2)return true;if(Main.Tiles[x,y].WallType!=0)WorldGen.KillWall(x,y);else Main.Tiles[x,y].WallType=2;return true;}
    }
}
namespace ImproveGame.Content.Projectiles
{
    /// <summary>模拟由出生所有者控制的逐格放墙机器人。</summary>
    internal sealed class WallRobot:Terraria.ModLoader.ModProjectile
    {
        public List<Point> Walls=new();
        public int index{get;set;}
        /// <summary>每帧仅处理一格，保留索引推进。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AI()
        {if(index>=Walls.Count)return;var p=Walls[index++];if(Main.Tiles[p.X,p.Y].WallType==2)return;
            WorldGen.KillWall(p.X,p.Y);Main.Tiles[p.X,p.Y].WallType=2;}
    }
}
namespace ImproveGame.Content.Functions.VeinMiner
{
    /// <summary>模拟明确矿工入口与实际扩展清单。</summary>
    internal static class VeinMinerSystem
    {
        public static int MinerIndex=-1;
        internal static List<Point> Targets=new();
        /// <summary>处理扩展矿格，允许重复条目以验证去重。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BreakAllOres(List<Point> points){foreach(var p in points)WorldGen.KillTile(p.X,p.Y);}
        /// <summary>仅由真实连锁任务入口提供矿工身份。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DoVeinMiningAt(Point point,int type)=>BreakAllOres(Targets);
    }
}
