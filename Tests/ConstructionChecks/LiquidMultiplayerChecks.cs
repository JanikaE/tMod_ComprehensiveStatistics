using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComprehensiveStatistics.Common.Networking;
using ComprehensiveStatistics.Common.Statistics;
using ComprehensiveStatistics.Common.Systems;
using ImproveGame.Content.Items;
using ImproveGame.Content.Packets;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Game=Terraria.Main;

/// <summary>以独立客户端/服务器格位投递真实协议，直接运行生产液体采集与服务器确认。</summary>
internal static class LiquidMultiplayerChecks
{
    private static readonly ImproveGameMultiplayerSystem Network=ModContent.GetInstance<ImproveGameMultiplayerSystem>();
    private static readonly StatisticsLiquidSystem LiquidSystem=ModContent.GetInstance<StatisticsLiquidSystem>();
    private static readonly ImproveGameToolSystem Tools=new();
    private static Tile[][,] worlds;
    private static int activeWorld;
    private static Guid client,server,otherClient,otherServer;
    private static int checks,cases;

    /// <summary>运行原版真实事件、服务器魔杖任务、预算、重放与可选依赖场景。</summary>
    internal static void Run()
    {
        Network.Load();LiquidSystem.Load();Tools.PostSetupContent();
        NativeChecks();WandChecks();ProtocolChecks();
        Tools.Unload();Network.Unload();LiquidSystem.Unload();
        Terraria.ModLoader.ModLoader.Available=false;
        Network.Load();LiquidSystem.Load();Reset();
        Check(ImproveGameMultiplayerSystem.NativeLiquidReady,"native liquid negotiates without ImproveGame");
        Check(!ImproveGameMultiplayerSystem.WandLiquidReady,"no false wand capability without optional mod");
        var native=NativeTool(206,()=>Change(4,4,0,255,true));Deliver(native);Expect(0,true,255);
        Network.Unload();LiquidSystem.Unload();Terraria.ModLoader.ModLoader.Available=true;
        Game.netMode=0;Game.dedServ=false;Game.myPlayer=0;Game.player=new[]{new Player()};
        Console.WriteLine($"LIQUID_MULTIPLAYER_CHECKS_OK: {cases} scenarios, {checks} assertions; independent worlds, native ingress, true server wand authority, two owners, rejected/competing/ordinary updates, replay, timeout, bounds, protocol 3 and optional dependency");
    }

    /// <summary>原版操作必须经服务器实际接受，预测、普通同步与客户端自报不能入账。</summary>
    private static void NativeChecks()
    {
        Reset();var packets=NativeTool(206,()=>Change(4,4,0,255,true));
        Check(packets.Select(p=>p.Bytes[0]).SequenceEqual(new byte[]{15,48,16}),"begin precedes native water; commit follows it");
        Check(Game.player[0].Statistics.Records.Count==0,"client prediction not credited");
        var credits=Deliver(packets);Expect(0,true,255);
        Apply(credits);Expect(0,true,255);Deliver(packets);Expect(0,true,255);
        Reset();Seed(4,4,0,100);packets=NativeTool(206,()=>Change(4,4,0,255,true));Deliver(packets);Expect(0,true,155);
        Reset();Seed(4,4,2,100);Seed(3,3,2,80);
        packets=NativeTool(5303,()=>{Change(4,4,2,0,true);Change(3,3,2,0,true);});Deliver(packets);Expect(2,false,180);
        for(int kind=0;kind<4;kind++)
        {
            Reset();int liquid=kind;int tool=new[]{3031,4820,5302,5364}[kind];
            packets=NativeTool(tool,()=>Change(4,4,liquid,255,true));Deliver(packets);Expect(kind,true,255);
            packets=NativeTool(5304,()=>Change(4,4,liquid,0,true));Deliver(packets);Expect(kind,false,255);
        }
        Reset();packets=NativeTool(206,()=>Change(4,4,0,255,true));Deliver(packets,rejectWater:true);Expect(0,true,0);
        Reset();Seed(4,4,0,255);packets=NativeTool(206,()=>Change(4,4,0,255,true));Deliver(packets);Expect(0,true,0);
        Reset();Change(4,4,0,255,true);packets=Take();Deliver(packets);Expect(0,true,0);
        Reset();Seed(5,4,0,80);
        packets=NativeTool(206,()=>
        {
            Change(4,4,0,255,true);
            WorldGen.LiquidFrame=()=>Change(5,4,0,0,true);
            WorldGen.SquareTileFrame(4,4);WorldGen.LiquidFrame=null;
        });
        Deliver(packets);Expect(0,true,255);Expect(0,false,0);
        Reset();Seed(4,4,0,100);Seed(5,4,0,80);Seed(3,4,0,50);
        packets=NativeTool(205,()=>
        {
            Change(4,4,0,0,true);
            global::System.Threading.Tasks.Task.Run(()=>
            {Change(5,4,0,0,false);NetMessage.SendData(48,number:5,number2:4);}).GetAwaiter().GetResult();
            Change(3,4,0,0,true);
        });Deliver(packets);Expect(0,false,150);
        Reset();packets=NativeTool(206,()=>Change(4,4,0,255,true));
        Deliver(packets,serverReaction:()=>Change(4,4,0,0,false));Expect(0,true,255);
        Reset();packets=NativeTool(206,()=>Change(4,4,0,255,true));
        ServerPacket(packets[0].Bytes,0);ServerPacket(Water(4,4,0,30),1);
        Deliver(packets.Skip(1).ToList());Expect(0,true,0);
        Reset();packets=NativeTool(206,()=>Change(4,4,0,255,true));
        ServerPacket(packets[0].Bytes,0);ServerPacket(packets[1].Bytes,0);Game.GameUpdateCount+=1801;
        Deliver(packets.Skip(2).ToList());Expect(0,true,0);
        Reset();packets=NativeTool(206,()=>Change(4,4,0,255,true));
        ServerPacket(packets[0].Bytes,0);ServerPacket(packets[1].Bytes,0);
        ServerPacket(packets[2].Bytes[..^1],0);Deliver(packets.Skip(2).ToList());Expect(0,true,0);
        Reset();packets=NativeTool(206,()=>Change(4,4,0,255,true));
        ServerPacket(packets[0].Bytes,0);ServerPacket(packets[1].Bytes,0);
        ServerPacket(Frame(StatisticsPacketType.NativeLiquidBegin,w=>{ImproveGameProtocol.WriteSession(w,otherClient,otherServer);
            w.Write(1UL);w.Write(8);w.Write(4);w.Write(206);}),1);
        ServerPacket(Water(8,4,0,128),1);
        ServerPacket(Frame(StatisticsPacketType.NativeLiquidCommit,w=>{ImproveGameProtocol.WriteSession(w,otherClient,otherServer);
            w.Write(1UL);w.Write((byte)1);w.Write((ushort)1);}),1);
        var both=Deliver(packets.Skip(2).ToList());Expect(0,true,255);
        Check(both.Select(p=>p.To).Order().SequenceEqual(new[]{0,1}),"two native operations retain different recipients");
        Reset();packets=NativeTool(206,()=>Change(4,4,0,255,true));
        ServerPacket(packets[0].Bytes,0);ServerPacket(packets[1].Bytes,0);ImproveGameMultiplayerSystem.Disconnect(0);
        Deliver(packets.Skip(2).ToList());Expect(0,true,0);
        Reset();packets=NativeTool(206,()=>Change(4,4,0,255,true));
        ServerPacket(packets[0].Bytes,0);ServerPacket(packets[1].Bytes,0);
        ServerPacket(Frame(StatisticsPacketType.NativeLiquidCommit,w=>{ImproveGameProtocol.WriteSession(w,client,server);w.Write(1UL);w.Write((byte)1);w.Write((ushort)32);}),0);
        Deliver(packets.Skip(2).ToList());Expect(0,true,0);
        Reset();packets=NativeTool(206,()=>
        {
            Change(4,4,0,255,true);WorldGen.LiquidFrame=()=>
            {for(int i=0;i<34;i++)NetMessage.sendWater(4,4);};
            WorldGen.SquareTileFrame(4,4);WorldGen.LiquidFrame=null;
        });Deliver(packets);Expect(0,true,0);
        Reset();packets=NativeTool(206,()=>Change(4,4,1,255,true));Deliver(packets);Expect(1,true,0);
        Reset();packets=NativeTool(206,()=>{Change(4,4,0,255,true);throw new InvalidOperationException();},expectThrow:true);
        Deliver(packets);Expect(0,true,255);
        Reset();Seed(4,4,0,40);packets=NativeTool(205,()=>Change(4,4,0,0,true));
        ServerPacket(packets[0].Bytes,0);Switch(2);Change(4,4,0,20,false);
        Deliver(packets.Skip(1).ToList());Expect(0,false,0);
    }

    /// <summary>真实上游接收 Hook 创建服务器任务，延后执行不得丢失拥有者或写入服务器人物数据。</summary>
    private static void WandChecks()
    {
        Reset();var task=StartWand(0,new LiquidWand(),new Rectangle(4,4,3,2));
        Switch(2);Drain(task.Pending);
        Check(Game.player.All(p=>p.Statistics.Records.Count==0),"server wand does not access personal stats");
        Apply(Flush());Expect(0,true,1530);
        task=StartWand(0,new LiquidWand{Absorb=true},new Rectangle(4,4,3,2));Switch(2);Drain(task.Pending);
        Apply(Flush());Expect(0,false,1530);
        Reset();Seed(4,4,0,30);Seed(5,4,1,40);Seed(6,4,2,50);
        task=StartWand(0,new LiquidWand{Absorb=true},new Rectangle(4,4,3,1));Switch(2);Drain(task.Pending);Apply(Flush());
        Expect(0,false,30);Expect(1,false,40);Expect(2,false,50);
        Reset();var first=StartWand(0,new LiquidWand(),new Rectangle(4,4,2,1));
        var second=StartWand(1,new LiquidWand{Kind=2},new Rectangle(6,4,2,1));
        Switch(2);first.Pending.MoveNext();second.Pending.MoveNext();first.Pending.MoveNext();second.Pending.MoveNext();
        var outputs=Flush();Check(outputs.Select(p=>p.To).Order().SequenceEqual(new[]{0,1}),"two wand owners receive separate batches");
        Apply(outputs);Expect(0,true,510);Expect(2,true,0);
        using(var body=Body(outputs.Single(p=>p.To==1).Bytes))
            Check(ImproveGameProtocol.TryReadBatch(body,otherClient,otherServer,out _,out var entries)&&entries.Sum(e=>e.Count)==510,"second owner credited independently");
        Reset();task=StartWand(0,new LiquidWand(),new Rectangle(4,4,3,1));Switch(2);task.Pending.MoveNext();Apply(Flush());Expect(0,true,255);
        Reset();task=StartWand(0,new LiquidWand(),new Rectangle(4,4,2,1));
        Network.OnWorldUnload();LiquidSystem.OnWorldUnload();Switch(2);Drain(task.Pending);Check(Flush().Count==0,"old server task cannot enter new world");
        Reset();Switch(2);var untrusted=new LiquidWand().ModifyTiles(Game.player[0],new Rectangle(4,4,1,1));Drain(untrusted);
        Check(Flush().Count==0,"server helper without accepted sender has no authority");
        Reset();var piece=MakeChange(0,255);task=StartWand(0,new LiquidWand(),new Rectangle(4,4,1,1),()=>
        {
            var authority=ImproveGameMultiplayerSystem.Capture(Game.player[0],ImproveGameCapability.Liquid);
            Check(authority!=null,"real receiver carries liquid authority");
            for(int i=0;i<5000;i++)authority.RecordLiquid(piece);
        });
        outputs=Flush();using(var body=Body(outputs.Single().Bytes))
        {Check(ImproveGameProtocol.TryReadBatch(body,client,server,out _,out var entries),"large liquid batch parses");
            Check(entries.Count==2&&entries.All(e=>e.Count<=ImproveGameProtocol.MaxCount)&&entries.Sum(e=>e.Count)==1_275_000,"raw volume splits without truncation");}
        Apply(outputs);Expect(0,true,1_275_000);
    }

    /// <summary>协议宽度、内容白名单、旧协议拒绝、未协商能力与旧随机会话均独立检查。</summary>
    private static void ProtocolChecks()
    {
        Reset();Switch(2);ServerPacket(Frame(StatisticsPacketType.ImproveGameHello,w=>
        {w.Write((ushort)2);w.Write(Guid.NewGuid().ToByteArray());w.Write((byte)255);}),1);
        Check(Take().Count==0,"protocol 2 not parsed as widened protocol 3");
        Check((ushort)ImproveGameCapability.Liquid==256&&(ushort)ImproveGameCapability.NativeLiquid==512,"capabilities do not truncate to byte");
        foreach(var content in new[]{new StableContentKey("Terraria","LiquidWater"),new StableContentKey("Terraria","LiquidLava"),
            new StableContentKey("Terraria","LiquidHoney"),new StableContentKey("Terraria","LiquidShimmer")})
            Check(ImproveGameProtocol.IsValid(new(TileChangeKind.LiquidPlaced,content,0,255)),"known liquid accepted");
        foreach(var entry in new[]{new ImproveGameCredit(TileChangeKind.LiquidPlaced,new("Other","LiquidWater"),0,1),
            new ImproveGameCredit(TileChangeKind.LiquidRemoved,new("Terraria","WireRed"),0,1),
            new ImproveGameCredit(TileChangeKind.LiquidPlaced,new("Terraria","LiquidWater"),1,1),
            new ImproveGameCredit(TileChangeKind.LiquidPlaced,new("Terraria","LiquidWater"),0,ImproveGameProtocol.MaxCount+1)})
            Check(!ImproveGameProtocol.IsValid(entry),"invalid liquid result rejected");
        var packets=NativeTool(206,()=>Change(4,4,0,255,true));
        Switch(1);ImproveGameMultiplayerSystem.EnterWorld();Deliver(packets);Expect(0,true,0);
        Reset();Switch(1);ImproveGameMultiplayerSystem.EnterWorld();
        packets=NativeTool(206,()=>Change(4,4,0,255,true));Check(packets.Count==1&&packets[0].Bytes[0]==48,"unconfirmed client only sends vanilla packet");
        Deliver(packets);Expect(0,true,0);
        Reset();var malformed=Frame(StatisticsPacketType.ImproveGameCreditBatch,w=>
        {
            ImproveGameProtocol.WriteSession(w,client,server);w.Write(1UL);w.Write(1UL);w.Write((ushort)2);
            w.Write((byte)TileChangeKind.LiquidPlaced);w.Write("Terraria");w.Write("LiquidWater");w.Write(0);w.Write(100L);
            w.Write((byte)TileChangeKind.LiquidRemoved);w.Write("Terraria");w.Write("WireRed");w.Write(0);w.Write(1L);
        });
        Apply(new(){(0,malformed)});Expect(0,true,0);
        packets=NativeTool(206,()=>Change(4,4,0,255,true));Deliver(packets);Expect(0,true,255);
        Reset();Guid limited=Guid.NewGuid();Switch(2);
        ServerPacket(Frame(StatisticsPacketType.ImproveGameHello,w=>{w.Write(ImproveGameProtocol.Version);w.Write(limited.ToByteArray());
            w.Write((ushort)ImproveGameCapability.NativeLiquid);}),1);
        var welcome=Take().Single();Guid limitedServer;using(var body=Body(welcome.Bytes))
        {body.ReadUInt16();ImproveGameProtocol.ReadIdentity(body);limitedServer=ImproveGameProtocol.ReadIdentity(body);}
        ServerPacket(Frame(StatisticsPacketType.ImproveGameReady,w=>ImproveGameProtocol.WriteSession(w,limited,limitedServer)),1);
        var denied=StartWand(1,new LiquidWand(),new Rectangle(4,4,1,1));Switch(2);Drain(denied.Pending);
        Check(Flush().Count==0,"native-only connection cannot receive wand authority");
    }

    /// <summary>每个场景重建随机会话和三份独立格位，第一客户端执行真实握手，第二连接模拟独立客户端身份。</summary>
    private static void Reset()
    {
        cases++;Network.OnWorldUnload();Network.OnWorldLoad();LiquidSystem.OnWorldUnload();LiquidSystem.OnWorldLoad();
        Game.player=new[]{new Player{whoAmI=0},new Player{whoAmI=1}};Game.GameUpdateCount=100;Game.gameMenu=false;
        worlds=new Tile[3][,];for(int n=0;n<3;n++){worlds[n]=new Tile[16,16];for(int x=0;x<16;x++)for(int y=0;y<16;y++)worlds[n][x,y]=new Tile();}
        activeWorld=2;for(int x=0;x<16;x++)for(int y=0;y<16;y++)Game.Tiles[x,y]=new Tile();
        WorldGen.LiquidFrame=null;ModPacket.Sent.Clear();Player.tileTargetX=Player.tileTargetY=4;
        Switch(1);ImproveGameMultiplayerSystem.EnterWorld();Network.PostUpdatePlayers();
        var hello=Take().Single();using(var body=Body(hello.Bytes)){body.ReadUInt16();client=ImproveGameProtocol.ReadIdentity(body);}
        ServerPacket(hello.Bytes,0);var welcome=Take().Single();using(var body=Body(welcome.Bytes))
        {body.ReadUInt16();ImproveGameProtocol.ReadIdentity(body);server=ImproveGameProtocol.ReadIdentity(body);}
        Apply(new(){welcome});var ready=Take().Single();ServerPacket(ready.Bytes,0);
        otherClient=Guid.NewGuid();ServerPacket(Frame(StatisticsPacketType.ImproveGameHello,w=>
        {w.Write(ImproveGameProtocol.Version);w.Write(otherClient.ToByteArray());w.Write((ushort)ImproveGameProtocol.All);}),1);
        var otherWelcome=Take().Single();using(var body=Body(otherWelcome.Bytes))
        {body.ReadUInt16();ImproveGameProtocol.ReadIdentity(body);otherServer=ImproveGameProtocol.ReadIdentity(body);}
        ServerPacket(Frame(StatisticsPacketType.ImproveGameReady,w=>ImproveGameProtocol.WriteSession(w,otherClient,otherServer)),1);
        Switch(1);Check(ImproveGameMultiplayerSystem.NativeLiquidReady,"native ready after complete handshake");
    }

    /// <summary>复制格位而不共享 Tile 引用，模式切换不会把客户端预测泄漏给服务器。</summary>
    private static void Switch(int mode)
    {
        int target=mode==2?2:0;
        if(worlds!=null){for(int x=0;x<16;x++)for(int y=0;y<16;y++)worlds[activeWorld][x,y]=Clone(Game.Tiles[x,y]);
            for(int x=0;x<16;x++)for(int y=0;y<16;y++)Game.Tiles[x,y]=Clone(worlds[target][x,y]);}
        activeWorld=target;Game.netMode=mode;Game.dedServ=mode==2;Game.myPlayer=mode==2?255:0;
    }
    /// <summary>液体检查只需独立保存液体标量，其他世界夹具不参与此工程的液体场景。</summary>
    private static Tile Clone(Tile tile)=>new(){LiquidType=tile.LiquidType,LiquidAmount=tile.LiquidAmount};
    /// <summary>同时准备真实服务器和两个客户端的场景前值。</summary>
    private static void Seed(int x,int y,int kind,int units){foreach(var world in worlds){world[x,y].LiquidType=kind;world[x,y].LiquidAmount=units;}Change(x,y,kind,units,false);}
    /// <summary>只修改当前执行端的实际格位。</summary>
    private static void Change(int x,int y,int kind,int units,bool send){Game.Tiles[x,y].LiquidType=kind;Game.Tiles[x,y].LiquidAmount=units;if(send)NetMessage.sendWater(x,y);}
    /// <summary>通过生产客户端原版工具入口采集全部标记与原版包。</summary>
    private static List<(int To,byte[] Bytes)> NativeTool(int tool,Action action,bool expectThrow=false)
    {
        Switch(1);Take();try{Game.player[0].UseLiquid(new Item{type=tool,LiquidAction=_=>action()});if(expectThrow)throw new Exception("missing original exception");}
        catch(InvalidOperationException)when(expectThrow){}return Take();
    }
    /// <summary>投递服务器原生消息或有界模组包；可以模拟服务器拒绝及帧修复反应。</summary>
    private static void ServerPacket(byte[] bytes,int sender,bool rejectWater=false,Action reaction=null)
    {
        Switch(2);WorldGen.LiquidFrame=reaction;
        if(bytes[0]==48)On_MessageBuffer.Deliver(sender,()=>{if(!rejectWater)NetMessage.Accept(bytes);},bytes);
        else On_MessageBuffer.Deliver(sender,()=>Handle(bytes,sender));
        WorldGen.LiquidFrame=null;
    }
    /// <summary>执行一批原版步骤的网络往返，只回传确认后的结果。</summary>
    private static List<(int To,byte[] Bytes)> Deliver(List<(int To,byte[] Bytes)> packets,bool rejectWater=false,Action serverReaction=null)
    {foreach(var packet in packets)ServerPacket(packet.Bytes,0,rejectWater,serverReaction);var result=Flush();Apply(result);return result;}
    /// <summary>只执行服务器确认包，不把第二名玩家包投递给本地角色冒充归属。</summary>
    private static void Apply(List<(int To,byte[] Bytes)> packets){Switch(1);foreach(var packet in packets)Handle(packet.Bytes,256);}
    /// <summary>清空已捕获数据。</summary>
    private static List<(int To,byte[] Bytes)> Take(){var packets=ModPacket.Sent.ToList();ModPacket.Sent.Clear();return packets;}
    /// <summary>触发生产有界回传。</summary>
    private static List<(int To,byte[] Bytes)> Flush(){Switch(2);Network.PostUpdateWorld();return Take();}
    /// <summary>按真实发起连接创建并返回延后执行的液体任务。</summary>
    private static SelectorItemOperation StartWand(int owner,LiquidWand wand,Rectangle area,Action after=null)
    {Switch(2);var packet=new SelectorItemOperation{Source=owner,Item=wand,Area=area,AfterCreate=after};On_MessageBuffer.Deliver(owner,packet.Receive);return packet;}
    /// <summary>构造一个实际原版消息，供竞争与无窗口场景使用。</summary>
    private static byte[] Water(int x,int y,int kind,int units){using var s=new MemoryStream();using var w=new BinaryWriter(s);w.Write((byte)48);w.Write((short)x);w.Write((short)y);w.Write((byte)units);w.Write((byte)kind);return s.ToArray();}
    /// <summary>构造经过生产会话解析的原始有界包。</summary>
    private static byte[] Frame(StatisticsPacketType type,Action<BinaryWriter> body)
    {using var s=new MemoryStream();using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))body(w);using var p=new MemoryStream();using var o=new BinaryWriter(p);o.Write((byte)type);o.Write((ushort)s.Length);o.Write(s.ToArray());return p.ToArray();}
    /// <summary>读取完整有界正文。</summary>
    private static BinaryReader Body(byte[] bytes){using var r=new BinaryReader(new MemoryStream(bytes));r.ReadByte();return new BinaryReader(new MemoryStream(r.ReadBytes(r.ReadUInt16())));}
    /// <summary>运行生产网络正文分派。</summary>
    private static void Handle(byte[] bytes,int sender){using var r=new BinaryReader(new MemoryStream(bytes));ImproveGameMultiplayerSystem.HandlePacket((StatisticsPacketType)r.ReadByte(),r,sender);}
    /// <summary>推进实际挂接的迭代器。</summary>
    private static void Drain(IEnumerator task){while(task.MoveNext()){} }
    /// <summary>构造原始体积事件以独立验证服务器队列分片。</summary>
    private static LiquidChange MakeChange(int kind,int units){LiquidSnapshot.TryCreate(kind,units,out var after);return LiquidChange.Between(default,after);}
    /// <summary>按最终客户端真实确认出口的方向、稳定键及原始长整数求和。</summary>
    private static void Expect(int kind,bool placed,long count)
    {
        string prefix=$"verified:{(placed?TileChangeKind.LiquidPlaced:TileChangeKind.LiquidRemoved)}:Liquid{new[]{"Water","Lava","Honey","Shimmer"}[kind]}:";
        long actual=Game.player[0].Statistics.Records.Where(r=>r.StartsWith(prefix,StringComparison.Ordinal)).Sum(r=>long.Parse(r.Split(':')[^1]));
        Check(actual==count,$"{prefix} expected {count}, got {actual}");
    }
    /// <summary>报告失败约束。</summary>
    private static void Check(bool value,string reason){if(!value)throw new Exception("LQ4: "+reason);checks++;}
}
