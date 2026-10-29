using System;
using System.Collections;
using System.Linq;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.Systems;
using ImproveGame.Content.Items;
using ImproveGame.Content.Functions.Construction;
using Terraria;
using Terraria.ModLoader;
using Game=Terraria.Main;
/// <summary>用真实 MonoMod Hook 执行生产适配器，验证任务关联、原函数转发与施工结果。</summary>
internal static class Program
{
    /// <summary>安装模拟上游入口并逐场景运行。</summary>
    private static void Main(string[] args)
    {
        // 允许以未来版本运行同一组生产 Hook / 双端回归，验证版本门槛不会挡住未变的入口。
        if (args.Length != 0) ModLoader.Upstream.Version = new Version(args[0]);
        var system=new ImproveGameConstructionSystem();
        system.PostSetupContent();
        if(Logger.Warnings!=0) throw new Exception("A hook group failed to install.");
        // 首先验证用户报告的单次建造；小型放置包装刻意允许内联，不能用 NoInlining 掩盖漏计。
        Reset(); new CreateWand().Use(Game.player[0],false); Expect("place:2");
        Reset(); new CreateWand().Use(Game.player[0],true); Expect("wall-place:2");
        Reset(); CreateWand.BulkWidth=3; WorldGen.CascadeToX=7;
        Game.MouseWorld=new Microsoft.Xna.Framework.MousePoint(5,4);
        for(int x=4;x<=7;x++) { Game.Tiles[x,4].HasTile=true; Game.Tiles[x,4].TileType=1; }
        new CreateWand().Use(Game.player[0],true);
        if(Game.player[0].Statistics.Records.Count(x=>x=="remove:1")!=3
            || Game.player[0].Statistics.Records.Count(x=>x=="wall-place:2")!=3)
            throw new Exception("Building cleanup missed selected collateral removals or included an outside target.");
        Reset(); CreateWand.BulkWidth=3; CreateWand.SkipWallIndex=1; WorldGen.CascadeToX=7;
        Game.MouseWorld=new Microsoft.Xna.Framework.MousePoint(5,4);
        for(int x=4;x<=7;x++) { Game.Tiles[x,4].HasTile=true; Game.Tiles[x,4].TileType=1; }
        new CreateWand().Use(Game.player[0],true);
        if(Game.player[0].Statistics.Records.Count(x=>x=="remove:1")!=2)
            throw new Exception("An unselected hole inside the building bounds was counted.");
        Reset(); GenerateCore.CleanupWidth=3; WorldGen.CascadeToX=8;
        for(int x=4;x<=8;x++) { Game.Tiles[x,4].HasTile=true; Game.Tiles[x,4].TileType=1; }
        new ConstructWand().StartUseItem(Game.player[0]);
        ConstructWand.Pending.MoveNext(); Drain((IEnumerator)ConstructWand.Pending.Current);
        Expect("remove:1","remove:1","remove:1","remove:1");
        if(Game.Tiles[8,4].HasTile) throw new Exception("Expected outside collateral destruction fixture.");
        Reset(); GenerateCore.CleanupWidth=3; GenerateCore.CleanupHeight=3;
        for(int x=4;x<=7;x++) for(int y=4;y<=7;y++) { Game.Tiles[x,y].HasTile=true; Game.Tiles[x,y].TileType=1; }
        new ConstructWand().StartUseItem(Game.player[0]);
        ConstructWand.Pending.MoveNext(); Drain((IEnumerator)ConstructWand.Pending.Current);
        Expect(Enumerable.Repeat("remove:1",16).ToArray());
        Reset(); GenerateCore.CleanupWidth=3; WorldGen.FailX=6;
        for(int x=4;x<=7;x++) { Game.Tiles[x,4].HasTile=true; Game.Tiles[x,4].TileType=1; }
        new ConstructWand().StartUseItem(Game.player[0]);
        ConstructWand.Pending.MoveNext(); Drain((IEnumerator)ConstructWand.Pending.Current);
        Expect("remove:1","remove:1","remove:1");
        Reset();
        var cell=Game.Tiles[4,4]; cell.HasTile=true; cell.TileType=1; cell.WallType=1;
        new ConstructWand().StartUseItem(Game.player[0]); Drain(ConstructWand.Pending);
        Expect("remove:1","wall-remove:1","wall-place:2","place:9","object-place:100:7",
            "wire:RedWire:True:1","wire:BlueWire:True:1","wire:GreenWire:True:1","wire:YellowWire:True:1","wire:Actuator:True:1");
        if(GenerateCore.ObjectMarker!=123) throw new Exception("Out TileObject did not reach upstream.");
        Reset(); ImproveGame.Helpers.MyUtils.NoMaterial=true;
        new ConstructWand().StartUseItem(Game.player[0]); Drain(ConstructWand.Pending);
        if(!Game.player[0].Statistics.Records.Contains("place:9"))
            throw new Exception("Direct grass restoration after failed material lookup was missed.");
        Reset(); cell=Game.Tiles[4,4]; cell.HasTile=true; cell.TileType=1;
        new CreateWand().Use(Game.player[0],false); Expect("remove:1","place:2");
        Reset(); cell=Game.Tiles[4,4]; cell.HasTile=true; cell.TileType=1; cell.WallType=1;
        new CreateWand().Use(Game.player[0],true); Expect("remove:1","wall-remove:1","wall-place:2");
        Reset(); Game.netMode=1;
        new ConstructWand().StartUseItem(Game.player[0]); Drain(ConstructWand.Pending); Expect();
        new CreateWand().Use(Game.player[0],false); Expect();
        Reset();
        new ConstructWand().StartUseItem(Game.player[0]); var root=ConstructWand.Pending;
        root.MoveNext(); // 已创建、尚未执行的清理子迭代器
        TileChangeScope.Reset(); system.OnWorldUnload(); system.OnWorldLoad();
        Drain((IEnumerator)root.Current); Drain(root); Expect();
        Reset(); cell=Game.Tiles[4,4]; cell.HasTile=true; cell.TileType=1;
        new ConstructWand().StartUseItem(Game.player[0]); root=ConstructWand.Pending;
        root.MoveNext(); Drain((IEnumerator)root.Current); Expect("remove:1");
        // 丢弃后续迭代器即模拟取消，普通无归属辅助调用不能继承刚才的任务。
        ImproveGame.Helpers.MyUtils.TryPlaceTile(8,8,new Item(),Game.player[0],false,false); Expect("remove:1");
        Reset(); GenerateCore.ThrowAfterPlace=true;
        new ConstructWand().StartUseItem(Game.player[0]);
        try { Drain(ConstructWand.Pending); throw new Exception("Expected upstream failure."); }
        catch(InvalidOperationException) { }
        Expect("wall-place:2","place:9");
        ImproveGame.Helpers.MyUtils.TryPlaceTile(8,8,new Item(),Game.player[0],false,false); Expect("wall-place:2","place:9");
        M3Checks.Run();
        M4Checks.Run();
        system.Unload();
        Reset(); new CreateWand().Use(Game.player[0],false); Expect();
        ModLoader.Available=false; system.PostSetupContent();
        ModLoader.Available=true; ModLoader.Upstream.Version=new Version(1,8,1);
        system.PostSetupContent();
        Reset(); new CreateWand().Use(Game.player[0],false); Expect();
        ModLoader.Available=true; ModLoader.Upstream.Version=new Version(99,0);
        system.PostSetupContent();
        Reset(); new CreateWand().Use(Game.player[0],false); Expect("place:2");
        system.Unload();
        Console.WriteLine("CONSTRUCTION_CHECKS_OK: construction, replacement, clearing, wires, out forwarding, multiplayer suppression, stale tasks, cancellation, exceptions, unload and optional dependency");
    }
    /// <summary>模拟上游 Runner 的递归嵌套推进。</summary>
    private static void Drain(IEnumerator iterator)
    { while(iterator.MoveNext()) if(iterator.Current is IEnumerator child) Drain(child); }
    /// <summary>重置隔离世界，不启动游戏。</summary>
    private static void Reset()
    {
        TileChangeScope.Reset(); Game.dedServ=Game.gameMenu=false; Game.myPlayer=0; Game.netMode=0;
        Game.player[0]=new Player(); GenerateCore.ThrowAfterPlace=false;
        ImproveGame.Helpers.MyUtils.NoMaterial=false;
        GenerateCore.CleanupWidth=GenerateCore.CleanupHeight=0;
        WorldGen.CascadeToX=WorldGen.FailX=-1;
        CreateWand.BulkWidth=1; Game.MouseWorld=new Microsoft.Xna.Framework.MousePoint(4,4);
        CreateWand.SkipWallIndex=-1;
        for(int x=0;x<16;x++) for(int y=0;y<16;y++) Game.Tiles[x,y]=new Tile();
    }
    /// <summary>比较真实生产采集器产生的提交序列。</summary>
    private static void Expect(params string[] expected)
    { if(!Game.player[0].Statistics.Records.SequenceEqual(expected)) throw new Exception("Unexpected records: "+string.Join(",",Game.player[0].Statistics.Records)); }
}

