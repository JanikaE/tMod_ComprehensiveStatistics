using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.Systems;
using ImproveGame.Content.Items;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Game=Terraria.Main;

/// <summary>直接执行生产单人采集器与真实 MonoMod Hook，隔离玩家和格位，不加载游戏存档。</summary>
internal static class Program
{
    private static readonly StatisticsLiquidSystem System=new();
    private static int checks;

    /// <summary>运行原版事件、跨帧任务、边界与卸载场景。</summary>
    private static void Main()
    {
        // tModLoader 在 PostSetupContent 之前已 JIT 上游；先重现该顺序，不能只验证冷方法。
        RuntimeHelpers.PrepareMethod(typeof(SelectorItem).GetMethod("DoSelector",BindingFlags.Instance|BindingFlags.NonPublic).MethodHandle);
        Type iteratorType=typeof(SelectorItem).GetMethod("ModifyTiles",BindingFlags.Instance|BindingFlags.NonPublic)
            .GetCustomAttribute<IteratorStateMachineAttribute>().StateMachineType;
        RuntimeHelpers.PrepareMethod(iteratorType.GetMethod("MoveNext",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).MethodHandle);
        System.Load();System.OnWorldLoad();
        System.InstallImproveGame(new Mod());
        Check(StatisticsLiquidSystem.WandInstalled&&Logger.Warnings==0,"liquid group installed");
        PreJitSelectionChecks();
        NativeChecks();WandChecks();BoundaryChecks();
        System.Unload();Reset();Use(_=>Set(4,4,0,255));Expect(0,0,0);
        Check(!StatisticsLiquidSystem.WandInstalled&&!StatisticsLiquidSystem.NativeBoundaryInstalled,"unload clears capability");
        Check(On_Player.ItemCheck_UseBuckets==null&&On_WorldGen.SquareTileFrame==null
            &&On_Liquid.Update==null&&On_Liquid.AddWater==null&&On_NetMessage.sendWater==null&&On_NetMessage.SendData==null,"unload detaches events");
        Drain(new LiquidWand().ModifyTiles(Game.player[0],new Rectangle(4,4,1,1)));Expect(0,0,0);
        var unsupported=new StatisticsLiquidSystem();unsupported.InstallImproveGame(new Mod{Version=new Version(1,8,1)});
        Check(!StatisticsLiquidSystem.WandInstalled,"minimum version respected");
        var future=new StatisticsLiquidSystem();future.InstallImproveGame(new Mod{Version=new Version(99,0)});
        Check(StatisticsLiquidSystem.WandInstalled,"future compatible signatures allowed");future.Unload();
        Console.WriteLine($"LIQUID_COLLECTION_CHECKS_OK: {checks} assertions; native targets, nested steps, natural-update exclusion, real wand hooks, identity, sessions, exceptions and unload");
    }

    /// <summary>从预编译的真实框选入口进入，复现工厂已内联时挂钩成功但漏掉任务身份的问题。</summary>
    private static void PreJitSelectionChecks()
    {
        Reset();var selected=new LiquidWand{Selection=new Rectangle(4,4,3,2),Infinite=true};
        selected.Launch(Game.player[0]);Drain(selected.Pending);Expect(0,1530,0);
        selected.Launch(Game.player[0]);Drain(selected.Pending);Expect(0,1530,0);
        selected.AbsorptionMode=true;selected.Launch(Game.player[0]);Drain(selected.Pending);Expect(0,1530,1530);
        selected.Launch(Game.player[0]);Drain(selected.Pending);Expect(0,1530,1530);
        Reset();var advanced=new LiquidWandAdvanced{LiquidMode=3};
        advanced.Launch(Game.player[0]);Drain(advanced.Pending);Expect(3,255,0);
        advanced.AbsorptionMode=true;advanced.Launch(Game.player[0]);Drain(advanced.Pending);Expect(3,255,255);
        Console.WriteLine("PREJIT_SELECTION_CHECKS_OK: root selection, six-cell fill, repeat, absorption and advanced shimmer");
    }

    /// <summary>原版目标集合、工具类型及环境边界检查。</summary>
    private static void NativeChecks()
    {
        foreach(int type in new[]{205,206,207,1128,3031,3032,4820,4872,5302,5303,5304,5364})
        {Reset();Use(_=>Set(4,4,0,255),type);Expect(0,255,0);}
        Reset();Set(4,4,0,100);Use(_=>Set(4,4,0,255));Expect(0,155,0);
        Reset();Set(4,4,0,255);Use(_=>Set(4,4,0,255));Expect(0,0,0);
        Reset();Use(_=>{});Expect(0,0,0);
        Reset();Set(4,4,0,100);Set(3,3,0,80);Set(5,5,0,75);
        Use(_=>{Set(4,4,0,0);WorldGen.SquareTileFrame(4,4);Set(3,3,0,0);Liquid.AddWater(3,3);Set(5,5,0,0);});Expect(0,0,255);
        Reset();Use(_=>{Set(4,4,0,10);Set(6,4,0,200);});Expect(0,10,0);
        Reset();Set(4,4,0,40);Set(5,4,2,80);
        WorldGen.Frame=()=>Set(5,4,2,0);
        Use(_=>{Set(4,4,0,255);WorldGen.SquareTileFrame(4,4);});Expect(0,215,0);Expect(2,0,0);
        Reset();Liquid.AddBody=()=>Set(4,4,0,100);Liquid.UpdateBody=()=>Set(4,4,0,25);
        Use(_=>{Set(4,4,0,255);Liquid.AddWater(4,4);new Liquid().Update();});Expect(0,255,0);
        new Liquid().Update();WorldGen.SquareTileFrame(4,4);Expect(0,255,0);
        Reset();Use(player=>{Set(4,4,0,60);Use(_=>Set(4,4,0,80));});Expect(0,80,0);
        Reset();Use(player=>{Set(4,4,0,10);Game.player[1].Use(Tool(_=>Set(4,4,0,100)));Set(4,4,0,130);});Expect(0,40,0);
        Check(Game.player[1].Statistics.Records.Count==0,"remote nested actor not recorded");
        Reset();try{Use(_=>{Set(4,4,0,60);throw new InvalidOperationException();});throw new Exception("missing exception");}
        catch(InvalidOperationException){}Expect(0,60,0);
        Check(!LiquidChangeScope.HasScope,"exception clears scope");Set(4,4,0,0);Expect(0,60,0);
        Reset();Set(4,4,4,100);Use(_=>Set(4,4,0,0));Expect(0,0,0);
        Reset();Use(_=>Set(4,4,4,100));Expect(0,0,0);
        Reset();Player.tileTargetX=Player.tileTargetY=0;Use(_=>Set(0,0,1,90));Expect(1,90,0);
        Reset();Use(_=>Set(4,4,0,100),999);Expect(0,0,0);
        Reset();Use(_=>{Set(4,4,0,30);System.OnWorldUnload();Set(4,4,0,60);});Expect(0,0,0);
        Check(!LiquidChangeScope.HasScope,"world reset not undone by finally");
    }

    /// <summary>逐格挂接、真实迭代器、取消与换手持检查。</summary>
    private static void WandChecks()
    {
        for(int kind=0;kind<4;kind++)
        {
            Reset();var wand=new LiquidWandAdvanced{LiquidMode=kind};Run(wand);Expect(kind,255,0);
            wand.AbsorptionMode=true;Run(wand);Expect(kind,255,255);
        }
        Reset();Set(4,4,0,100);Run(new LiquidWand{Storage=37});Expect(0,37,0);
        Reset();Set(4,4,0,100);Run(new LiquidWand{Storage=0});Expect(0,0,0);
        Reset();Set(4,4,0,100);Run(new LiquidWand{Storage=0,Infinite=true});Expect(0,155,0);
        Reset();Set(4,4,1,100);Run(new LiquidWand{LiquidMode=0});Expect(0,0,0);Expect(1,0,0);
        Reset();Set(4,4,0,30);Set(5,4,1,40);Set(6,4,2,50);
        Run(new LiquidWand{AbsorptionMode=true,Storage=1000,Cap=1000},3);Expect(0,0,30);Expect(1,0,40);Expect(2,0,50);
        Reset();Set(4,4,3,80);Run(new LiquidWand{AbsorptionMode=true});Expect(3,0,0);
        Reset();Set(4,4,0,255);Run(new LiquidWand());Expect(0,0,0);
        Reset();Run(new LiquidWand{ReturnFalse=true});Expect(0,255,0);
        Reset();var task=new LiquidWand().ModifyTiles(Game.player[0],new Rectangle(4,4,3,1));
        Check(task.MoveNext(),"first task step yielded");Expect(0,255,0);
        Game.player[0].HeldItem=new object();Check(task.MoveNext(),"second step yielded after held change");Expect(0,510,0);
        Check(Game.Tiles[6,4].LiquidAmount==0,"unexecuted cancelled portion untouched");
        Reset();var throwing=new LiquidWand{ThrowX=5};
        try{Run(throwing,3);throw new Exception("missing wand exception");}catch(InvalidOperationException){}
        Expect(0,510,0);Check(!LiquidChangeScope.HasScope,"wand exception clears step");
        throwing.ThrowX=-1;throwing.ModifySelectedTiles(Game.player[0],6,4);Expect(0,510,0);
        Reset();var post=new LiquidWand();WorldGen.Frame=()=>Set(4,4,0,0);post.Post=()=>WorldGen.SquareTileFrame(4,4);
        Run(post);Expect(0,255,0);
        Reset();Run(new LiquidWand{Custom=(player,x,y)=>{Set(x,y,0,60);Use(_=>Set(x,y,0,100));}});Expect(0,100,0);
        Reset();var unbound=new LiquidWand();unbound.ModifySelectedTiles(Game.player[0],4,4);Expect(0,0,0);
        Reset();task=new LiquidWand().ModifyTiles(Game.player[0],new Rectangle(4,4,1,1));
        System.OnWorldUnload();System.OnWorldLoad();Drain(task);Expect(0,0,0);
        Reset();task=new LiquidWand().ModifyTiles(Game.player[0],new Rectangle(4,4,1,1));
        Player old=Game.player[0];Game.player[0]=new Player{whoAmI=0};Drain(task);Expect(0,0,0);
        Check(old.Statistics.Records.Count==0,"same-slot replacement not credited to old player");
        Reset();var outside=new LiquidWand{Custom=(player,x,y)=>{Set(x,y,0,20);Set(x+1,y,0,99);}};
        Run(outside);Expect(0,20,0);
    }

    /// <summary>所有未确认多人和无效角色必须仅转发，不产生统计。</summary>
    private static void BoundaryChecks()
    {
        foreach(int mode in new[]{1,2})
        {
            Reset();Game.netMode=mode;Use(_=>Set(4,4,0,100));Run(new LiquidWand());Expect(0,0,0);
            Check(Game.Tiles[4,4].LiquidAmount==255,"multiplayer original behavior forwarded");
        }
        Reset();Game.gameMenu=true;Use(_=>Set(4,4,0,10));Run(new LiquidWand());Expect(0,0,0);
        Reset();Game.dedServ=true;Use(_=>Set(4,4,0,10));Run(new LiquidWand());Expect(0,0,0);
        Reset();Game.player[0].active=false;Use(_=>Set(4,4,0,10));Run(new LiquidWand());Expect(0,0,0);
        Reset();var other=Game.player[1];other.Use(Tool(_=>Set(4,4,0,100)));
        Drain(new LiquidWand().ModifyTiles(other,new Rectangle(4,4,1,1)));Expect(0,0,0);
        Check(other.Statistics.Records.Count==0,"other player never credited");
        Reset();Game.netMode=1;IEnumerator task=new LiquidWand().ModifyTiles(Game.player[0],new Rectangle(4,4,1,1));
        Game.netMode=0;Drain(task);Expect(0,0,0);
    }

    /// <summary>每个独立场景更换会话与角色，不能共用上一个场景的累计。</summary>
    private static void Reset()
    {
        System.OnWorldUnload();System.OnWorldLoad();Game.netMode=Game.myPlayer=0;Game.gameMenu=Game.dedServ=false;
        Game.player=new[]{new Player{whoAmI=0},new Player{whoAmI=1}};Player.tileTargetX=Player.tileTargetY=4;
        WorldGen.Frame=Liquid.UpdateBody=Liquid.AddBody=null;
        for(int x=0;x<16;x++)for(int y=0;y<16;y++)Game.Tiles[x,y]=new Tile();
    }
    /// <summary>建立原版工具动作。</summary>
    private static Item Tool(Action<Player> action,int type=ItemID.WaterBucket)=>new(){type=type,Execute=action};
    /// <summary>通过真实生产事件处理器执行原版夹具。</summary>
    private static void Use(Action<Player> action,int type=ItemID.WaterBucket)=>Game.player[0].Use(Tool(action,type));
    /// <summary>修改夹具的实际可变格位。</summary>
    private static void Set(int x,int y,int kind,int amount){Game.Tiles[x,y].LiquidType=kind;Game.Tiles[x,y].LiquidAmount=amount;}
    /// <summary>从真实被挂接工厂启动选区。</summary>
    private static void Run(LiquidWand wand,int width=1)=>Drain(wand.ModifyTiles(Game.player[0],new Rectangle(4,4,width,1)));
    /// <summary>推进真实编译器生成的迭代器。</summary>
    private static void Drain(IEnumerator iterator){while(iterator.MoveNext()){} }
    /// <summary>按液体类型聚合生产输出，检查方向值而不依赖中途 Flush 次数。</summary>
    private static void Expect(int kind,long placed,long removed)
    {
        var records=Game.player[0].Statistics.Records;
        Check(records.Where(value=>(int)value.PlacedKind==kind).Sum(value=>(long)value.PlacedUnits)==placed,"placed volume for "+kind);
        Check(records.Where(value=>(int)value.RemovedKind==kind).Sum(value=>(long)value.RemovedUnits)==removed,"removed volume for "+kind);
        Check(records.All(value=>!value.IsZero),"no zero records");
    }
    /// <summary>记录断言数并报告具体约束。</summary>
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
}
