using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.Systems;
using Microsoft.Xna.Framework;
using Terraria;
using ImproveGame.Content.Items;
using ImproveGame.Content.Projectiles;
using ImproveGame.Content.Functions.VeinMiner;
using Game=Terraria.Main;
/// <summary>使用真实生产适配器和 MonoMod 执行 M3 行为回归。</summary>
internal static class M3Checks
{
    /// <summary>逐组验证来源、替换、取消、异常和多人抑制。</summary>
    internal static void Run()
    {
        var system=new ImproveGameToolSystem();int warnings=Terraria.ModLoader.Logger.Warnings;
        system.PostSetupContent();
        if(Terraria.ModLoader.Logger.Warnings!=warnings)throw new Exception("M3 group installation failed.");
        Reset();Put(4);bool sound=false;var consumed=new Dictionary<int,int>();var selected=new HashSet<Point>{new(4,4)};
        SpaceWand.OperateTile(Game.player[0],4,4,selected,PlaceType.Soild,Terraria.ID.BlockType.Solid,ref sound,consumed);
        Expect("remove:1","place:2");if(!sound||consumed[2]!=1)throw new Exception("Space ref or enum forwarding failed.");
        Reset();Put(4);SpaceWand.OperateTile(Game.player[0],4,4,selected,PlaceType.Soild,Terraria.ID.BlockType.Slope,ref sound,consumed);Expect();
        Reset();Put(4);SpaceWand.RebuildSame=true;
        SpaceWand.OperateTile(Game.player[0],4,4,selected,PlaceType.Soild,Terraria.ID.BlockType.Solid,ref sound,consumed);
        Expect("remove:1","place:1");
        Reset();Put(4);SpaceWand.Fail=true;SpaceWand.OperateTile(Game.player[0],4,4,selected,PlaceType.Soild,Terraria.ID.BlockType.Solid,ref sound,consumed);Expect();
        Reset();Put(4);SpaceWand.ThrowAfterPlace=true;
        try{SpaceWand.OperateTile(Game.player[0],4,4,selected,PlaceType.Soild,Terraria.ID.BlockType.Solid,ref sound,consumed);throw new Exception("Expected upstream failure.");}
        catch(InvalidOperationException){}
        Expect("remove:1","place:2");Put(8);WorldGen.KillTile(8,4);Expect("remove:1","place:2");
        Reset();Put(4);SpaceWand.OperateTile(new Player{whoAmI=1},4,4,selected,PlaceType.Soild,Terraria.ID.BlockType.Solid,ref sound,consumed);Expect();
        Reset();for(int x=4;x<=7;x++){Put(x);Game.Tiles[x,4].WallType=1;}WorldGen.CascadeToX=7;
        new StarburstWand().UseFixed(Game.player[0]);
        if(Count("remove:1")!=3||Count("wall-remove:1")!=3)throw new Exception("Fixed burst counts or bounds failed.");
        Reset();for(int x=4;x<7;x++)Put(x);
        Drain(new MagickWand{WallMode=false}.ModifyTiles(Game.player[0],new Rectangle(4,4,3,1)));
        Expect("remove:1","remove:1","remove:1");
        Reset();var selector=new WallPlaceSelectorMode();Game.Tiles[4,4].WallType=1;
        Drain(selector.ModifyTiles(Game.player[0],new Rectangle(4,4,1,1)));Expect("wall-remove:1");
        Drain(selector.ModifyTiles(Game.player[0],new Rectangle(4,4,1,1)));Expect("wall-remove:1","wall-place:2");
        Reset();Put(4);Put(5);var iterator=new MagickWand().ModifyTiles(Game.player[0],new Rectangle(4,4,2,1));
        iterator.MoveNext();Expect("remove:1"); // 取消后不推进，不可虚构另一格拆除。
        TileChangeScope.Reset();system.OnWorldUnload();system.OnWorldLoad();Drain(iterator);Expect("remove:1");
        Reset();var robot=Robot();Game.Tiles[4,4].WallType=1;Game.Tiles[5,4].WallType=1;
        ImproveGameToolSystem.ObserveRobotSpawn(robot.Projectile);robot.AI();robot.AI();
        Expect("wall-remove:1","wall-place:2","wall-remove:1","wall-place:2");
        Reset();robot=Robot();ImproveGameToolSystem.ObserveRobotSpawn(robot.Projectile);
        TileChangeScope.Reset();system.OnWorldUnload();robot.AI();Expect();
        Reset();robot=Robot();robot.AI();Expect(); // 未观察出生，不猜测归属。
        Reset();Put(4);Put(5);VeinMinerSystem.MinerIndex=0;VeinMinerSystem.Targets=new(){new(4,4),new(5,4),new(5,4)};
        using(TileChangeScope.Begin(Game.player[0]))
        {TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Remove);WorldGen.KillTile(4,4);VeinMinerSystem.DoVeinMiningAt(new Point(4,4),1);}
        Expect("remove:1","remove:1");
        Reset();Put(4);VeinMinerSystem.MinerIndex=-1;VeinMinerSystem.Targets=new(){new(4,4)};
        VeinMinerSystem.DoVeinMiningAt(new Point(4,4),1);Expect();
        Reset();Game.netMode=1;Put(4);Game.Tiles[4,4].WallType=1;
        new StarburstWand().UseFixed(Game.player[0]);
        Drain(new WallPlaceSelectorMode().ModifyTiles(Game.player[0],new Rectangle(4,4,1,1)));
        SpaceWand.OperateTile(Game.player[0],4,4,selected,PlaceType.Soild,Terraria.ID.BlockType.Solid,ref sound,consumed);
        robot=Robot();ImproveGameToolSystem.ObserveRobotSpawn(robot.Projectile);robot.AI();
        VeinMinerSystem.MinerIndex=0;VeinMinerSystem.Targets=new(){new(4,4)};VeinMinerSystem.DoVeinMiningAt(new Point(4,4),1);Expect();
        system.Unload();Reset();Put(4);new StarburstWand().UseFixed(Game.player[0]);Expect();
        Console.WriteLine("M3_CHECKS_OK: space byte enum/ref, dual replacements, same-type rebuild, slope, fixed/selected burst, phased walls, robot identity, stale/canceled tasks, vein dedup, multiplayer and unload");
    }
    /// <summary>构造模拟机器人，不依赖当前手持物品。</summary>
    private static WallRobot Robot()
    {var robot=new WallRobot{Walls=new(){new(4,4),new(5,4)}};robot.Projectile.owner=0;robot.Projectile.ModProjectile=robot;return robot;}
    /// <summary>填充一格普通地形。</summary>
    private static void Put(int x){Game.Tiles[x,4].HasTile=true;Game.Tiles[x,4].TileType=1;}
    /// <summary>递归推进上游协程。</summary>
    private static void Drain(IEnumerator iterator){while(iterator.MoveNext())if(iterator.Current is IEnumerator child)Drain(child);}
    /// <summary>读取某类提交数。</summary>
    private static int Count(string value)=>Game.player[0].Statistics.Records.Count(x=>x==value);
    /// <summary>比较精确的提交序列。</summary>
    private static void Expect(params string[] expected)
    {if(!Game.player[0].Statistics.Records.SequenceEqual(expected))throw new Exception("M3 unexpected records: "+string.Join(",",Game.player[0].Statistics.Records));}
    /// <summary>重置独立世界及运行模式。</summary>
    private static void Reset()
    {TileChangeScope.Reset();Game.gameMenu=Game.dedServ=false;Game.netMode=0;Game.myPlayer=0;Game.player[0]=new Player();WorldGen.CascadeToX=WorldGen.FailX=-1;
        SpaceWand.Fail=SpaceWand.RebuildSame=SpaceWand.ThrowAfterPlace=false;for(int x=0;x<16;x++)for(int y=0;y<16;y++)Game.Tiles[x,y]=new Tile();}
}
