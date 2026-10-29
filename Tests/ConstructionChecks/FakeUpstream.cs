using System;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;
namespace ImproveGame.Helpers
{
    /// <summary>模拟已审计辅助入口，所有方法由真实 MonoMod Hook 拦截。</summary>
    internal static class MyUtils
    {
        internal static bool NoMaterial;
        internal static bool SyncMining;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Item PickItemFromArray(Player player,Item[] items,Func<Item,bool> predicate,bool consume)
        { if(NoMaterial) return null; foreach(var item in items) if(predicate(item)) return item; return null; }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool TryKillTile(int x,int y,Player player)
        { WorldGen.KillTile(x,y);if(SyncMining&&Main.netMode==1)NetMessage.SendKill(x,y);return !Main.Tiles[x,y].HasTile; }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool TryPlaceTile(int x,int y,Item item,Player player,bool mute,bool forced)
            =>WorldGen.PlaceTile(x,y,item.createTile,mute,forced,player.whoAmI);
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool BongBongPlace(int x,int y,Item item,Player player,bool mute,bool forced,bool sound)
        { TryKillTile(x,y,player); return TryPlaceTile(x,y,item,player,mute,forced); }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int PlaceChestNoSync(int x,int y,ushort type,bool near,int style) { MakeObject(x,y); return 0; }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool Place3x2NoSyncDresser(int x,int y,ushort type,int style) { MakeObject(x,y); return true; }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool TryPlaceMultiTileDirect(int x,int y,int type,int style,int direction,out TileObject data)
        { MakeObject(x,y); data=new TileObject{Marker=123}; return true; }
        /// <summary>创建测试家具。</summary>
        private static void MakeObject(int x,int y)
        {
            for(int dx=0;dx<2;dx++) for(int dy=0;dy<2;dy++)
            { var t=Main.Tiles[x+dx,y+dy]; t.HasTile=true; t.TileType=100; t.Style=7; t.OriginX=x; t.OriginY=y; }
        }
    }
}
namespace ImproveGame.Content.Items
{
    /// <summary>模拟构造物品真实发起入口。</summary>
    internal sealed class ConstructWand
    {
        internal static IEnumerator Pending;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool StartUseItem(Player player)
        { Pending=Functions.Construction.GenerateCore.Generate(new TagCompound(),new Point(4,4)); return true; }
    }
    /// <summary>模拟建造工具，包括为铺墙单独清理实体物块的路径。</summary>
    internal sealed class CreateWand
    {
        internal static int BulkWidth=1;
        internal static int SkipWallIndex=-1;
        /// <summary>模拟建筑模板中明确需要铺墙的格位。</summary>
        internal readonly record struct TileInfo(bool HasWall);
        /// <summary>指定模拟建筑是否测试铺墙清理。</summary>
        internal sealed class BuildingData
        {
            internal bool Wall;
            public int Width => BulkWidth;
            public int Height => 1;
            public TileInfo[] TileInfos => Enumerable.Range(0,Width).Select(i=>new TileInfo(Wall && i!=SkipWallIndex)).ToArray();
        }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryPlaceBuilding(Player player,BuildingData data)
        {
            for(int x=4;x<4+data.Width;x++)
                if(data.Wall) { if(data.TileInfos[x-4].HasWall) TryPlaceWall(new Item(),player,x,4); }
                else TryPlace(new Item(),player,x,4);
            return true;
        }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryPlace(Item item,Player player,int x,int y)
            => Helpers.MyUtils.BongBongPlace(x,y,item,player,true,true,false);
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryPlaceWall(Item item,Player player,int x,int y)
        { Helpers.MyUtils.TryKillTile(x,y,player); WorldGen.KillWall(x,y); Main.Tiles[x,y].WallType=item.createWall; return true; }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Use(Player player,bool wall) => TryPlaceBuilding(player,new BuildingData{Wall=wall});
    }
}
namespace ImproveGame.Content.Functions.Construction
{
    /// <summary>仅用于匹配真实结构阶段的参数身份。</summary>
    internal sealed class QoLStructure { public short Width = 0, Height = 0; }
    /// <summary>模拟分帧嵌套流水线，实际迭代器类型由 C# 编译器生成。</summary>
    internal static class GenerateCore
    {
        internal static bool ThrowAfterPlace;
        internal static int ObjectMarker;
        internal static short CleanupWidth, CleanupHeight;
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        public static IEnumerator Generate(TagCompound tag,Point point)
        {
            var data=new QoLStructure { Width=CleanupWidth, Height=CleanupHeight };
            yield return KillTiles(data,point);
            yield return GenerateWalls(data,point);
            yield return GenerateSingleTiles(data,point);
            yield return GenerateMultiTiles(data,point);
            yield return GenerateOutSet(data,point);
            yield return SquareTiles(data,point);
            yield return TextSigns(data,point);
        }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        public static IEnumerator KillTiles(QoLStructure data,Point p)
        {
            int processed=0;
            for(int x=p.X;x<=p.X+data.Width;x++) for(int y=p.Y;y<=p.Y+data.Height;y++)
            {
                if(Main.Tiles[x,y].HasTile) Helpers.MyUtils.TryKillTile(x,y,Main.player[0]);
                if(Main.Tiles[x,y].WallType!=0) WorldGen.KillWall(x,y);
                if(++processed%4==0) yield return null;
            }
            yield return null;
        }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        public static IEnumerator GenerateWalls(QoLStructure data,Point p)
        { TryPlaceWall(new Item(),p.X,p.Y); yield return null; }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryPlaceWall(Item item,int x,int y) { Main.Tiles[x,y].WallType=item.createWall; return true; }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        public static IEnumerator GenerateSingleTiles(QoLStructure data,Point p)
        {
            Point placePosition=p;
            Helpers.MyUtils.PickItemFromArray(Main.player[0],new[]{new Item()},
                item=>Helpers.MyUtils.TryPlaceTile(placePosition.X,placePosition.Y,item,Main.player[0],true,true),true);
            Main.Tiles[p.X,p.Y].HasTile=true;
            Main.Tiles[p.X,p.Y].TileType=9;
            if(ThrowAfterPlace) throw new InvalidOperationException("injected failure");
            yield return null;
        }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        public static IEnumerator GenerateMultiTiles(QoLStructure data,Point p)
        {
            Helpers.MyUtils.TryPlaceMultiTileDirect(6,6,100,7,1,out var result);
            ObjectMarker=result.Marker;
            yield return null;
        }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        public static IEnumerator GenerateOutSet(QoLStructure data,Point p)
        {
            WorldGen.PlaceWire(p.X,p.Y); WorldGen.PlaceWire2(p.X,p.Y); WorldGen.PlaceWire3(p.X,p.Y);
            WorldGen.PlaceWire4(p.X,p.Y); WorldGen.PlaceActuator(p.X,p.Y); yield return null;
        }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        public static IEnumerator SquareTiles(QoLStructure data,Point p) { yield break; }
        /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
        public static IEnumerator TextSigns(QoLStructure data,Point p) { yield break; }
    }
}

