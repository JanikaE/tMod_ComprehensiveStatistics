using System;
using System.Linq;
using ComprehensiveStatistics.Common.Statistics;
using Terraria;
using Game = Terraria.Main;

/// <summary>对生产快照与作用域运行确定性的行为回归；不使用真实存档。</summary>
internal static class Program
{
    private static int checks;
    private static Player Local => Game.player[0];
    private static Tile Cell => Game.Tiles[4, 4];

    /// <summary>依次验证真实成功、失败、嵌套、对象及会话边界。</summary>
    private static void Main()
    {
        Check("place and repeat", () =>
        {
            Step(TileChangeIntent.Place, () => Put(1));
            Step(TileChangeIntent.Place, () => Put(1));
        }, "place:1");
        Check("failed placement", () => Step(TileChangeIntent.Place, () => { }));
        Check("nested same target", () => Step(TileChangeIntent.Place,
            () => Step(TileChangeIntent.Place, () => Put(2))), "place:2");
        Check("final grass identity", () => Step(TileChangeIntent.Place, () =>
        {
            Put(1);
            Step(TileChangeIntent.Place, () => Put(3));
        }), "place:3");
        Check("replace counts both sides without nested duplication", () =>
        {
            Put(1);
            Step(TileChangeIntent.Replace, () =>
            {
                Step(TileChangeIntent.Remove, () => Cell.HasTile = false);
                Put(2);
            });
        }, "remove:1", "place:2");
        Check("failed replacement after removal", () =>
        {
            Put(1);
            Step(TileChangeIntent.Replace, () => Cell.HasTile = false);
        }, "remove:1");
        Check("cleanup and rebuild are independent", () =>
        {
            Put(1);
            Step(TileChangeIntent.Remove, () => Cell.HasTile = false);
            Step(TileChangeIntent.Place, () => Put(1));
        }, "remove:1", "place:1");
        Check("failed mining", () => { Put(1); Step(TileChangeIntent.Remove, () => { }); });
        Check("shape and paint only", () =>
        {
            Put(1);
            Step(TileChangeIntent.Place, () => { Cell.Slope = 1; Cell.Color = 4; });
        });
        Check("complete object once for all parts", () =>
        {
            using (TileChangeScope.Begin(Local))
            {
                ForObject((x, y) => TileChangeScope.Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Place));
                MakeObject();
            }
        }, "object-place:100:7");
        Check("partial object placement", () =>
            Step(TileChangeIntent.Place, () => MakeObject(true)));
        Check("object removal once", () =>
        {
            MakeObject();
            using (TileChangeScope.Begin(Local))
            {
                ForObject((x, y) => TileChangeScope.Observe(x, y, TileChangeLayer.Tile, TileChangeIntent.Remove));
                ForObject((x, y) => Game.Tiles[x, y].HasTile = false);
            }
        }, "object-remove:100:7");
        Check("object remnant blocks removal", () =>
        {
            MakeObject();
            Step(TileChangeIntent.Remove, () => Cell.HasTile = false);
        });
        Check("unknown object does not become terrain", () =>
            Step(TileChangeIntent.Place, () => { MakeObject(); Cell.Style = -1; }));
        Check("wall replacement", () =>
        {
            Cell.WallType = 1;
            Step(TileChangeIntent.Replace, () => Cell.WallType = 2, TileChangeLayer.Wall);
        }, "wall-remove:1", "wall-place:2");
        Check("failed wall replacement leaves removal", () =>
        {
            Cell.WallType = 1;
            Step(TileChangeIntent.Replace, () => Cell.WallType = 0, TileChangeLayer.Wall);
        }, "wall-remove:1");
        Check("tile and wall have independent intents", () =>
        {
            Put(1);
            using (TileChangeScope.Begin(Local))
            {
                TileChangeScope.Observe(4, 4, TileChangeLayer.Tile, TileChangeIntent.Remove);
                TileChangeScope.Observe(4, 4, TileChangeLayer.Wall, TileChangeIntent.Place);
                Cell.HasTile = false;
                Cell.WallType = 2;
            }
        }, "remove:1", "wall-place:2");
        Check("no scope", () => { TileChangeScope.Observe(4, 4, TileChangeLayer.Tile, TileChangeIntent.Place); Put(1); });
        Check("remote player", () =>
        {
            using (TileChangeScope.Begin(new Player { whoAmI = 1 }))
            {
                TileChangeScope.Observe(4, 4, TileChangeLayer.Tile, TileChangeIntent.Place);
                Put(1);
            }
        });
        Check("dedicated server", () => { Game.dedServ = true; Step(TileChangeIntent.Place, () => Put(1)); });
        Check("menu", () => { Game.gameMenu = true; Step(TileChangeIntent.Place, () => Put(1)); });
        Check("world switch invalidates pending scope", () =>
            Step(TileChangeIntent.Place, () => { Put(1); TileChangeScope.Reset(); }));
        Check("player instance replaced", () =>
        {
            Player old = Local;
            Step(TileChangeIntent.Place, () => { Put(1); Game.player[0] = new Player(); });
            if (old.Statistics.Records.Count != 0) throw new Exception("Stale player received statistics.");
        });
        Check("exception keeps actual change and restores context", () =>
        {
            try { Step(TileChangeIntent.Place, () => { Put(1); throw new InvalidOperationException(); }); }
            catch (InvalidOperationException) { }
            TileChangeScope.Observe(5, 5, TileChangeLayer.Tile, TileChangeIntent.Place);
            Game.Tiles[5, 5].HasTile = true;
        }, "place:1");
        Check("dispose twice", () =>
        {
            TileChangeScope scope = TileChangeScope.Begin(Local);
            TileChangeScope.Observe(4, 4, TileChangeLayer.Tile, TileChangeIntent.Place);
            Put(1);
            scope.Dispose();
            scope.Dispose();
        }, "place:1");
        Check("nested foreign scope cannot acquire local ownership", () =>
        {
            using (TileChangeScope.Begin(Local))
            using (TileChangeScope.Begin(new Player { whoAmI = 1 }))
            {
                TileChangeScope.Observe(4, 4, TileChangeLayer.Tile, TileChangeIntent.Place);
                Put(1);
            }
        });
        Check("out of world", () =>
        {
            using (TileChangeScope.Begin(Local))
                TileChangeScope.Observe(-1, 4, TileChangeLayer.Tile, TileChangeIntent.Place);
        });
        Check("snapshot stores component bits by value", () =>
        {
            Cell.RedWire = Cell.BlueWire = Cell.GreenWire = Cell.YellowWire = Cell.HasActuator = true;
            TileContentSnapshot before = TileContentSnapshot.Capture(4, 4);
            Cell.RedWire = Cell.BlueWire = Cell.GreenWire = Cell.YellowWire = Cell.HasActuator = false;
            if (before.Wires != 31 || TileContentSnapshot.Capture(4, 4).Wires != 0)
                throw new Exception("The component snapshot retained mutable world state.");
        });
        Check("suppression propagates into original mining scope", () =>
        {
            using (TileChangeScope.Suppress())
                Step(TileChangeIntent.Place, () => Put(1));
        });
        Check("independent cleanup and rebuild under outer scope", () =>
        {
            Put(1);
            using (TileChangeScope.Begin(Local))
            {
                using (TileChangeScope.BeginStep(Local))
                {
                    TileChangeScope.Observe(4, 4, TileChangeLayer.Tile, TileChangeIntent.Remove);
                    Cell.HasTile = false;
                }
                using (TileChangeScope.BeginStep(Local))
                {
                    TileChangeScope.Observe(4, 4, TileChangeLayer.Tile, TileChangeIntent.Place);
                    Put(1);
                }
            }
        }, "remove:1", "place:1");
        Check("component placement deduplicates same target", () =>
        {
            using (TileChangeScope.Begin(Local))
            {
                TileChangeScope.Observe(4, 4, TileChangeLayer.Wiring, TileChangeIntent.Place);
                Cell.RedWire = true;
                TileChangeScope.Observe(4, 4, TileChangeLayer.Wiring, TileChangeIntent.Place);
                Cell.BlueWire = Cell.HasActuator = true;
            }
        }, "wire:RedWire:True:1", "wire:BlueWire:True:1", "wire:Actuator:True:1");
        Check("unchanged components do not count", () =>
        {
            Cell.RedWire = true;
            Step(TileChangeIntent.Place, () => Cell.Color = 5, TileChangeLayer.Wiring);
        });
        Check("trusted target excludes nested environmental placement", () =>
        {
            using (TileChangeScope.BeginStep(Local, directTargetsOnly: true))
            {
                TileChangeScope.Observe(4, 4, TileChangeLayer.Tile, TileChangeIntent.Place, direct: true);
                Put(1);
                TileChangeScope.Observe(5, 5, TileChangeLayer.Tile, TileChangeIntent.Place);
                Game.Tiles[5, 5].HasTile = true;
                Game.Tiles[5, 5].TileType = 2;
            }
        }, "place:1");
        Check("same wall removed then rebuilt", () =>
        {
            Cell.WallType=1;
            using(TileChangeScope.Begin(Local))
            {
                TileChangeScope.Observe(4,4,TileChangeLayer.Wall,TileChangeIntent.Replace);
                Cell.WallType=0;TileChangeScope.NoteRemoval(4,4,TileChangeLayer.Wall);Cell.WallType=1;
            }
        }, "wall-remove:1", "wall-place:1");
        Check("same object rebuilt counts each side once", () =>
        {
            MakeObject();
            using(TileChangeScope.Begin(Local))
            {
                ForObject((x,y)=>TileChangeScope.Observe(x,y,TileChangeLayer.Tile,TileChangeIntent.Replace));
                ForObject((x,y)=>Game.Tiles[x,y].HasTile=false);
                ForObject((x,y)=>TileChangeScope.NoteRemoval(x,y,TileChangeLayer.Tile));
                MakeObject();
            }
        }, "object-remove:100:7", "object-place:100:7");
        Check("object style replacement counts both styles", () =>
        {
            MakeObject();
            using(TileChangeScope.Begin(Local))
            {
                ForObject((x,y)=>TileChangeScope.Observe(x,y,TileChangeLayer.Tile,TileChangeIntent.Replace));
                ForObject((x,y)=>Game.Tiles[x,y].Style=8);
            }
        }, "object-remove:100:7", "object-place:100:8");
        Check("server replacement uses sink and nested native scope",()=>
        {
            Game.netMode=2;Game.dedServ=true;Put(1);
            var results=new System.Collections.Generic.List<TileChangeResult>();
            using(TileChangeScope.BeginVerified(Local,results.Add))
            using(TileChangeScope.Begin(Local))
            {TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Replace,direct:true);Put(2);}
            if(!results.SequenceEqual(new[]{new TileChangeResult(TileChangeKind.TileRemoved,1),new TileChangeResult(TileChangeKind.TilePlaced,2)}))
                throw new Exception("Server replacement did not use the shared dual result pipeline.");
        });
        Check("server complete object rebuild deduplicates each side",()=>
        {
            Game.netMode=2;Game.dedServ=true;MakeObject();
            var results=new System.Collections.Generic.List<TileChangeResult>();
            using(TileChangeScope.BeginVerified(Local,results.Add))
            {
                ForObject((x,y)=>TileChangeScope.Observe(x,y,TileChangeLayer.Tile,TileChangeIntent.Replace,direct:true));
                ForObject((x,y)=>Game.Tiles[x,y].HasTile=false);
                ForObject((x,y)=>TileChangeScope.NoteRemoval(x,y,TileChangeLayer.Tile));MakeObject();
            }
            if(!results.SequenceEqual(new[]{new TileChangeResult(TileChangeKind.ObjectRemoved,100,7),new TileChangeResult(TileChangeKind.ObjectPlaced,100,7)}))
                throw new Exception("Server object sides were lost or duplicated.");
        });
        Check("server stale scope has no result",()=>
        {
            Game.netMode=2;Game.dedServ=true;
            using(TileChangeScope.BeginVerified(Local,_=>throw new Exception("Stale server scope emitted a result.")))
            {TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Place,direct:true);Put(2);TileChangeScope.Reset();}
        });
        Console.WriteLine($"TILE_CHANGE_CHECKS_OK: {checks} behavioral cases");
    }

    /// <summary>在隔离世界中运行一项并精确比较提交结果。</summary>
    private static void Check(string name, Action action, params string[] expected)
    {
        TileChangeScope.Reset();
        Game.gameMenu = Game.dedServ = false;
        Game.netMode = 0;
        Game.myPlayer = 0;
        Game.player[0] = new Player();
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                Game.Tiles[x, y] = new Tile();
        action();
        string[] actual = Local.Statistics.Records.ToArray();
        if (!actual.SequenceEqual(expected))
            throw new Exception($"{name}: expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
        checks++;
    }

    /// <summary>执行一个最小同步步骤。</summary>
    private static void Step(TileChangeIntent intent, Action action, TileChangeLayer layer = TileChangeLayer.Tile)
    {
        using (TileChangeScope.Begin(Local))
        {
            TileChangeScope.Observe(4, 4, layer, intent);
            action();
        }
    }

    /// <summary>修改测试地形。</summary>
    private static void Put(int type) { Cell.HasTile = true; Cell.TileType = type; }

    /// <summary>遍历测试家具的完整组成范围。</summary>
    private static void ForObject(Action<int, int> action)
    {
        for (int x = 4; x < 6; x++)
            for (int y = 4; y < 6; y++) action(x, y);
    }

    /// <summary>创建完整或残缺的多格家具。</summary>
    private static void MakeObject(bool partial = false)
    {
        ForObject((x, y) =>
        {
            Tile tile = Game.Tiles[x, y];
            tile.HasTile = !partial || (x == 4 && y == 4);
            tile.TileType = 100;
            tile.Style = 7;
            tile.OriginX = tile.OriginY = 4;
        });
    }
}
