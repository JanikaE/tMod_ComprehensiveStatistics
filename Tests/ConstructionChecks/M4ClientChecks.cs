using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.Systems;
using ImproveGame.Content.Items;
using ImproveGame.Content.Projectiles;
using ImproveGame.Content.Functions.Construction;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Game=Terraria.Main;

/// <summary>独立客户端/服务器世界通过真实生产协议确认客户端施工，不共享格位引用。</summary>
internal static class M4ClientChecks
{
    private static ImproveGameMultiplayerSystem network;
    private static int checks;

    /// <summary>验证四类客户端工具、真实中间边界、拒绝、交错和异常。</summary>
    internal static void Run(ImproveGameMultiplayerSystem system)
    {
        network=system;network.OnWorldLoad();ClientMode();ImproveGameMultiplayerSystem.EnterWorld();network.PostUpdatePlayers();
        byte[] hello=Take().Single().Bytes;ServerPacket(hello,0);byte[] welcome=Take().Single().Bytes;
        ClientPacket(welcome);ServerPacket(Take().Single().Bytes,0);

        Case("building replacement",()=>Put(4,1),()=>new CreateWand().Use(Game.player[0],false),
            "verified:TileRemoved:Tile1:0:1","verified:TilePlaced:Tile2:0:1");
        Case("authoritative mining drops preserved",()=>{Put(4,1);ImproveGame.Helpers.MyUtils.SyncMining=true;},
            ()=>new CreateWand().Use(Game.player[0],false),"verified:TileRemoved:Tile1:0:1","verified:TilePlaced:Tile2:0:1");
        Case("building wall clears block and replaces wall",()=>{Put(4,1);Game.Tiles[4,4].WallType=1;},()=>new CreateWand().Use(Game.player[0],true),
            "verified:TileRemoved:Tile1:0:1","verified:WallRemoved:Wall1:0:1","verified:WallPlaced:Wall2:0:1");
        Case("server rejected all client changes",()=>Put(4,1),()=>new CreateWand().Use(Game.player[0],false),Array.Empty<string>(),accept:false);
        Case("same-type removal and rebuild",()=>Put(4,1),Rebuild,
            "verified:TileRemoved:Tile1:0:1","verified:TilePlaced:Tile1:0:1");
        Case("same-type net state without accepted gap",()=>Put(4,1),Rebuild,Array.Empty<string>(),ignoreEmpty:true);
        Case("only old removal succeeded",()=>Put(4,1),()=>
        {
            using var lease=ImproveGameMultiplayerSystem.BeginClientStep(Game.player[0],ImproveGameCapability.Building);
            TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Replace,true);WorldGen.KillTile(4,4);
        },"verified:TileRemoved:Tile1:0:1");
        Case("failed operation leaves identical content",()=>Put(4,1),()=>
        {
            using var lease=ImproveGameMultiplayerSystem.BeginClientStep(Game.player[0],ImproveGameCapability.Building);
            TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Replace,true);WorldGen.KillTile(4,4,fail:true);
        });
        Case("complete furniture style replacement",()=>Object(7),()=>
        {
            using var lease=ImproveGameMultiplayerSystem.BeginClientStep(Game.player[0],ImproveGameCapability.Building);
            for(int x=4;x<6;x++)for(int y=4;y<6;y++)TileChangeScope.Observe(x,y,TileChangeLayer.Tile,TileChangeIntent.Replace,true);
            Object(8);
        },"verified:ObjectRemoved:Tile100:7:1","verified:ObjectPlaced:Tile100:8:1");
        Case("partial furniture cannot count as placement",()=>{},()=>
        {
            using var lease=ImproveGameMultiplayerSystem.BeginClientStep(Game.player[0],ImproveGameCapability.Building);
            TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Place,true);
            Object(7);Game.Tiles[5,5].HasTile=false;
        });
        Case("construction phases preserve old and final grass",()=>Put(4,9),()=>
        {new ConstructWand().StartUseItem(Game.player[0]);Drain(ConstructWand.Pending);},
            "verified:TileRemoved:Tile9:0:1","verified:WallPlaced:Wall2:0:1","verified:TilePlaced:Tile9:0:1",
            "verified:ObjectPlaced:Tile100:7:1","verified:WiringPlaced:WireRed:0:1","verified:WiringPlaced:WireBlue:0:1",
            "verified:WiringPlaced:WireGreen:0:1","verified:WiringPlaced:WireYellow:0:1","verified:WiringPlaced:Actuator:0:1");
        Case("fixed burst bounded selection",()=>{for(int x=4;x<8;x++)Put(x,1);WorldGen.CascadeToX=7;},
            ()=>new StarburstWand().UseFixed(Game.player[0]),"verified:TileRemoved:Tile1:0:3");
        Case("selected collateral caught in native recursive kill",()=>{for(int x=4;x<7;x++)Put(x,1);WorldGen.CascadeToX=6;},()=>
        {
            using var lease=ImproveGameMultiplayerSystem.BeginClientStep(Game.player[0],ImproveGameCapability.FixedBurst);
            TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Remove,true);
            TileChangeScope.Observe(5,4,TileChangeLayer.Tile,TileChangeIntent.Remove,true);
            WorldGen.KillTile(4,4);
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write((byte)17);writer.Write((byte)0);writer.Write((short)4);writer.Write((short)4);writer.Write((short)0);writer.Write((byte)0);
            ModPacket.Sent.Add((-1,stream.ToArray()));
        },"verified:TileRemoved:Tile1:0:2");
        Case("robot replacement bound at spawn",()=>Game.Tiles[4,4].WallType=1,()=>
        {
            var robot=new WallRobot{Walls=new(){new Point(4,4)}};robot.Projectile.owner=0;robot.Projectile.ModProjectile=robot;
            ImproveGameToolSystem.ObserveRobotSpawn(robot.Projectile);robot.AI();
        },"verified:WallRemoved:Wall1:0:1","verified:WallPlaced:Wall2:0:1");
        Case("robot without observed spawn",()=>Game.Tiles[4,4].WallType=1,()=>
        {var robot=new WallRobot{Walls=new(){new Point(4,4)}};robot.Projectile.owner=0;robot.Projectile.ModProjectile=robot;robot.AI();});
        Case("cancel retains executed cleanup",()=>Put(4,1),()=>
        {new ConstructWand().StartUseItem(Game.player[0]);ConstructWand.Pending.MoveNext();Drain((IEnumerator)ConstructWand.Pending.Current);},
            "verified:TileRemoved:Tile1:0:1");
        Case("exception retains actual completed step",()=>GenerateCore.ThrowAfterPlace=true,()=>
        {new ConstructWand().StartUseItem(Game.player[0]);try{Drain(ConstructWand.Pending);}catch(InvalidOperationException){}},
            "verified:WallPlaced:Wall2:0:1","verified:TilePlaced:Tile9:0:1");
        Case("exact target holes exclude environmental change",()=>{for(int x=4;x<7;x++)Put(x,1);},()=>
        {
            using var lease=ImproveGameMultiplayerSystem.BeginClientStep(Game.player[0],ImproveGameCapability.Building);
            TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Replace,true);
            TileChangeScope.Observe(6,4,TileChangeLayer.Tile,TileChangeIntent.Replace,true);
            Put(4,2);Put(6,2);Game.Tiles[5,4].HasTile=false;
        },"verified:TileRemoved:Tile1:0:2","verified:TilePlaced:Tile2:0:2");
        Case("other player accepted packet cannot confirm owner",()=>Put(4,1),()=>new CreateWand().Use(Game.player[0],false),
            Array.Empty<string>(),nativeSender:1);
        Case("interleaved external edit invalidates own evidence",()=>Put(4,1),()=>new CreateWand().Use(Game.player[0],false),
            Array.Empty<string>(),conflict:true);
        Case("unregistered ordinary native synchronization",()=>Put(4,1),()=>{Put(4,2);NetMessage.SendTileSquare(0,4,4,1,1);});
        Case("world invalidation drops old client step",()=>Put(4,1),()=>
        {
            using var lease=ImproveGameMultiplayerSystem.BeginClientStep(Game.player[0],ImproveGameCapability.Building);
            TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Replace,true);Put(4,2);TileChangeScope.Reset();
        });
        Case("malformed candidate fragment cannot commit",()=>Put(4,1),()=>new CreateWand().Use(Game.player[0],false));
        Case("disconnect before commit drops pending window",()=>Put(4,1),()=>new CreateWand().Use(Game.player[0],false));
        Console.WriteLine($"M4_CLIENT_CHECKS_OK: {checks} cases; independent worlds, construction, building, fixed burst, robot, gaps, wire components, rejection, exact masks, cancellation, exception and replay");
    }

    /// <summary>执行同类型真实拆后重建，不用最终净差伪造拆除。</summary>
    private static void Rebuild()
    {
        using var lease=ImproveGameMultiplayerSystem.BeginClientStep(Game.player[0],ImproveGameCapability.Building);
        TileChangeScope.Observe(4,4,TileChangeLayer.Tile,TileChangeIntent.Replace,true);
        WorldGen.KillTile(4,4);WorldGen.PlaceTile(4,4,1);
    }

    /// <summary>运行一个场景并验证客户端只在服务器确认后累计；服务器有独立初始世界。</summary>
    private static void Case(string name,Action setup,Action execute,params string[] expected)
        =>Case(name,setup,execute,expected,true,false,0,false);

    /// <summary>可选择拒绝、忽略中间空缺、换来源或插入竞争编辑，验证不猜测数量。</summary>
    private static void Case(string name,Action setup,Action execute,string[] expected,bool accept=true,bool ignoreEmpty=false,int nativeSender=0,bool conflict=false)
    {
        ClientMode();Game.player[0].Statistics.Records.Clear();WorldGen.CascadeToX=WorldGen.FailX=-1;
        CreateWand.BulkWidth=1;CreateWand.SkipWallIndex=-1;GenerateCore.CleanupWidth=GenerateCore.CleanupHeight=0;
        GenerateCore.ThrowAfterPlace=false;ImproveGame.Helpers.MyUtils.NoMaterial=false;Game.MouseWorld=new MousePoint(4,4);
        ImproveGame.Helpers.MyUtils.SyncMining=false;WorldGen.ServerDrops=0;
        for(int x=0;x<16;x++)for(int y=0;y<16;y++)Game.Tiles[x,y]=new Tile();
        Take();setup();byte[] before=Save();execute();byte[] after=Save();
        if(Game.player[0].Statistics.Records.Count!=0)throw new Exception(name+": client prediction was recorded before confirmation");
        var outgoing=Take();Restore(before);ServerMode();bool inserted=false;
        foreach(var packet in outgoing)
        {
            if(packet.Bytes[0]==20||packet.Bytes[0]==17)
            {
                bool empty=packet.Bytes[0]==20&&NativeTargetEmpty(packet.Bytes);
                On_MessageBuffer.Deliver(nativeSender,()=>{if(accept&&(!ignoreEmpty||!empty))NetMessage.Accept(packet.Bytes);},packet.Bytes);
                if(conflict&&!inserted)
                {inserted=true;On_MessageBuffer.Deliver(1,()=>Put(4,51));}
            }
            else
            {
                if(name=="malformed candidate fragment cannot commit"&&packet.Bytes[0]==(byte)StatisticsPacketType.ImproveGameClientTargets)
                {packet.Bytes[49]=1;packet.Bytes[50]=1;}
                if(name=="disconnect before commit drops pending window"&&packet.Bytes[0]==(byte)StatisticsPacketType.ImproveGameClientCommit)
                    ImproveGameMultiplayerSystem.Disconnect(0);
                ServerPacket(packet.Bytes,0);
            }
        }
        if(name=="fixed burst bounded selection"&&outgoing.Count(p=>p.Bytes[0]==20)!=1)
            throw new Exception("Fixed clearing did not batch its completed boundary.");
        if(name=="authoritative mining drops preserved"&&(WorldGen.ServerDrops!=1
            || outgoing.FindIndex(p=>p.Bytes[0]==17)>outgoing.FindIndex(p=>p.Bytes[0]==20)))
            throw new Exception("Compatibility boundary overtook native mining or suppressed authoritative drops.");
        network.PostUpdateWorld();var results=Take();
        Restore(after);ClientMode();
        foreach(var packet in results)
        {
            if(packet.To!=0)throw new Exception(name+": result was sent to a different player");
            ClientPacket(packet.Bytes);ClientPacket(packet.Bytes); // 每个确认包重放一次，必须不重复。
        }
        var actual=Game.player[0].Statistics.Records.Order().ToArray();
        if(!actual.SequenceEqual(expected.Order()))throw new Exception(name+": expected ["+string.Join(",",expected)+"], got ["+string.Join(",",actual)+"]");
        checks++;
    }

    /// <summary>识别针对测试格位的实际空缺包，以模拟服务器未接受中间阶段。</summary>
    private static bool NativeTargetEmpty(byte[] bytes)
    {
        using var r=new BinaryReader(new MemoryStream(bytes));r.ReadByte();int x=r.ReadInt16(),y=r.ReadInt16(),w=r.ReadByte(),h=r.ReadByte();r.ReadByte();
        bool empty=false;for(int i=x;i<x+w;i++)for(int j=y;j<y+h;j++)
        {var tile=NetMessage.ReadTile(r);if(i==4&&j==4&&!tile.HasTile)empty=true;}
        return empty;
    }

    /// <summary>冻结完整隔离世界，防止客户端与服务器共享可变 Tile。</summary>
    private static byte[] Save()
    {using var s=new MemoryStream();using var w=new BinaryWriter(s);for(int x=0;x<16;x++)for(int y=0;y<16;y++)NetMessage.WriteTile(w,Game.Tiles[x,y]);return s.ToArray();}
    /// <summary>恢复一端的独立格位状态。</summary>
    private static void Restore(byte[] bytes)
    {using var r=new BinaryReader(new MemoryStream(bytes));for(int x=0;x<16;x++)for(int y=0;y<16;y++)Game.Tiles[x,y]=NetMessage.ReadTile(r);}
    /// <summary>经真实入站连接上下文调用生产协议。</summary>
    private static void ServerPacket(byte[] bytes,int sender){ServerMode();On_MessageBuffer.Deliver(sender,()=>Handle(bytes,sender));}
    /// <summary>在所属客户端处理生产结果。</summary>
    private static void ClientPacket(byte[] bytes){ClientMode();Handle(bytes,256);}
    /// <summary>解析模组包的类型并调用真实生产处理器。</summary>
    private static void Handle(byte[] bytes,int sender)
    {using var r=new BinaryReader(new MemoryStream(bytes));ImproveGameMultiplayerSystem.HandlePacket((StatisticsPacketType)r.ReadByte(),r,sender);}
    /// <summary>切换到本地客户端。</summary>
    private static void ClientMode(){Game.netMode=1;Game.dedServ=false;Game.myPlayer=0;Game.gameMenu=false;}
    /// <summary>切换专用服务器；读取 LocalPlayer 或 ModPlayer 都会令测试失败。</summary>
    private static void ServerMode(){Game.netMode=2;Game.dedServ=true;Game.myPlayer=255;}
    /// <summary>移出已发送字节，保留模组包与原版包之间的实际发送顺序。</summary>
    private static List<(int To,byte[] Bytes)> Take(){var result=ModPacket.Sent.ToList();ModPacket.Sent.Clear();return result;}
    /// <summary>修改测试物块。</summary>
    private static void Put(int x,int type){Game.Tiles[x,4].HasTile=true;Game.Tiles[x,4].TileType=type;}
    /// <summary>形成完整模拟家具；每格保留共同的规范原点与样式。</summary>
    private static void Object(int style)
    {for(int x=4;x<6;x++)for(int y=4;y<6;y++)Game.Tiles[x,y]=new Tile{HasTile=true,TileType=100,Style=style,OriginX=4,OriginY=4};}
    /// <summary>推进真实构造子迭代器。</summary>
    private static void Drain(IEnumerator value){while(value.MoveNext())if(value.Current is IEnumerator child)Drain(child);}
}
