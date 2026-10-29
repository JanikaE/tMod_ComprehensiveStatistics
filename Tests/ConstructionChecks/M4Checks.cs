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
using ImproveGame.Content.Functions.VeinMiner;
using ImproveGame.Packets.Tiles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Game=Terraria.Main;

/// <summary>运行真实生产 M4 协议、任务身份及 MonoMod Hook，所有世界和网络均隔离。</summary>
internal static class M4Checks
{
    private static ImproveGameMultiplayerSystem network;
    private static Guid client0,server0;
    private static int cases;

    /// <summary>验证两名玩家、跨帧任务、去重、断线及不受信执行端。</summary>
    internal static void Run()
    {
        Game.player=new[]{new Player{whoAmI=0},new Player{whoAmI=1}};
        Game.GameUpdateCount=100;Game.gameMenu=false;TileChangeScope.Reset();
        for(int x=0;x<16;x++)for(int y=0;y<16;y++)Game.Tiles[x,y]=new Tile();
        var tools=new ImproveGameToolSystem();network=ModContent.GetInstance<ImproveGameMultiplayerSystem>();
        int warnings=Logger.Warnings;
        tools.PostSetupContent();network.Load();network.PostSetupContent();network.OnWorldLoad();
        Assert(Logger.Warnings==warnings,"all receiver hooks installed");
        Game.netMode=1;Game.dedServ=false;Game.myPlayer=0;
        ImproveGameMultiplayerSystem.EnterWorld();network.OnWorldLoad();network.PostUpdatePlayers();
        Assert(ImproveGameMultiplayerSystem.GetDisplayStatus().State==ImproveGameCompatibilityState.WaitingForServer,
            "UI snapshot waits for real server negotiation");
        var hello=Take().Single();Assert(hello.To==-1,"client hello goes to server");
        using(var frame=Body(hello.Bytes)) {frame.ReadUInt16();client0=ImproveGameProtocol.ReadIdentity(frame);}
        Server(hello.Bytes,0);var welcome=Take().Single();
        using(var frame=Body(welcome.Bytes)) {frame.ReadUInt16();ImproveGameProtocol.ReadIdentity(frame);server0=ImproveGameProtocol.ReadIdentity(frame);}
        Put(4);Space(0);Assert(Flush().Count==0,"welcome without ready cannot authorize work");
        Client(welcome.Bytes);var ready=Take().Single();Server(ready.Bytes,0);
        Game.netMode=1;Game.dedServ=false;
        Assert(ImproveGameMultiplayerSystem.GetDisplayStatus().State==ImproveGameCompatibilityState.MultiplayerReady
            && ImproveGameMultiplayerSystem.GetDisplayStatus().EnabledCount==8,"UI snapshot reflects negotiated capabilities");
        Guid client1=Guid.NewGuid();Server(Frame(StatisticsPacketType.ImproveGameHello,w=>
        {w.Write(ImproveGameProtocol.Version);w.Write(client1.ToByteArray());w.Write((byte)ImproveGameProtocol.All);}),1);
        var welcome1=Take().Single();Guid server1;
        using(var frame=Body(welcome1.Bytes)){frame.ReadUInt16();ImproveGameProtocol.ReadIdentity(frame);server1=ImproveGameProtocol.ReadIdentity(frame);}
        Server(Frame(StatisticsPacketType.ImproveGameReady,w=>ImproveGameProtocol.WriteSession(w,client1,server1)),1);

        Put(4);Space(0);var result=Flush().Single();
        Assert(result.To==0,"server result only sent to task owner");
        Assert(Game.player.All(p=>p.Statistics.Records.Count==0),"server never writes character statistics");
        using(var frame=Body(result.Bytes))
        {
            Assert(ImproveGameProtocol.TryReadBatch(frame,client0,server0,out var sequence,out var entries)
                && sequence==1 && entries.Sum(e=>e.Count)==2,"server replacement contains both actual sides");
        }
        Client(result.Bytes);Assert(Game.player[0].Statistics.Records.Count==2,"owner applies confirmed sides");
        Client(result.Bytes);Assert(Game.player[0].Statistics.Records.Count==2,"duplicate batch rejected");
        Client(welcome.Bytes);Take();Client(result.Bytes);
        Assert(Game.player[0].Statistics.Records.Count==2,"duplicate welcome does not reset dedup cursor");
        Put(4);Space(1);result=Flush().Single();Assert(result.To==1,"second player uses separate session");
        Client(result.Bytes);Assert(Game.player[0].Statistics.Records.Count==2,"other player session rejected");

        // 任务创建发生在入站入口，逐帧执行已不在网络调用栈上；两人的工作必须仍然独立。
        ServerMode();for(int x=4;x<7;x++)Put(x);
        var first=new SelectorItemOperation{Source=0};var second=new SelectorItemOperation{Source=1};
        On_MessageBuffer.Deliver(0,first.Receive);On_MessageBuffer.Deliver(1,second.Receive);
        first.Pending.MoveNext();second.Pending.MoveNext(); // 第二名玩家面对已空首格，不得重复。
        second.Pending.MoveNext();first.Pending.MoveNext();
        var interleaved=Flush();
        Assert(interleaved.Count==2 && interleaved.Select(p=>p.To).Order().SequenceEqual(new[]{0,1}),"interleaved selector tasks preserve owners");
        foreach(var entry in interleaved)
        using(var frame=Body(entry.Bytes))
        {
            Guid c=entry.To==0?client0:client1,s=entry.To==0?server0:server1;
            Assert(ImproveGameProtocol.TryReadBatch(frame,c,s,out _,out var credits) && credits.Sum(e=>e.Count)==1,
                "interleaved selection credits only the player who actually removed each tile");
        }
        Drain(first.Pending);Drain(second.Pending);Flush();

        ServerMode();Put(4);new SpaceWandOperation{Source=0}.Receive();
        Assert(Flush().Count==0,"direct Receive without real network sender has no authority");
        Put(4);On_MessageBuffer.Deliver(1,new SpaceWandOperation{Source=0}.Receive);
        Assert(Flush().Count==0,"spoofed Sender cannot borrow another connection");
        Put(4);new StarburstWand().UseFixed(Game.player[0]);
        Assert(Flush().Count==0,"client fixed mode has no server proof and remains suppressed");
        Put(4);VeinMinerSystem.Targets=new(){new(4,4),new(5,4),new(5,4)};Put(5);
        On_MessageBuffer.Deliver(0,new VeinMiningPacket{Source=0}.Receive);
        result=Flush().Single();using(var frame=Body(result.Bytes))
        {Assert(ImproveGameProtocol.TryReadBatch(frame,client0,server0,out _,out var entries)&&entries.Sum(e=>e.Count)==2,"vein expansion duplicates removed once");}

        // 模拟服务器任务异常；已发生的改动保留，下一次没有身份的入口不能继承上下文。
        Put(4);SpaceWand.ThrowAfterPlace=true;
        try{Space(0);throw new Exception("Expected upstream exception.");}catch(InvalidOperationException){}
        SpaceWand.ThrowAfterPlace=false;Assert(Flush().Count==1,"actual results survive upstream exception");
        Put(4);new SpaceWandOperation{Source=0}.Receive();Assert(Flush().Count==0,"exception releases server context");

        Put(4);var idle=new SelectorItemOperation{Source=0};On_MessageBuffer.Deliver(0,idle.Receive);
        Game.GameUpdateCount+=1801;Drain(idle.Pending);
        Assert(Flush().Count==0,"idle task expires rather than borrowing a live session");
        Put(4);Space(0);Game.GameUpdateCount+=1801;
        Assert(Flush().Count==0,"queued results expire within the bounded retention window");
        Put(4);var old=new SelectorItemOperation{Source=0};On_MessageBuffer.Deliver(0,old.Receive);
        ImproveGameMultiplayerSystem.Disconnect(0);Drain(old.Pending);
        Assert(Flush().Count==0,"disconnect invalidates already scheduled tasks");
        Put(4);Space(0);Assert(Flush().Count==0,"disconnected session cannot create tasks");
        Server(Frame(StatisticsPacketType.ImproveGameHello,w=>{w.Write((ushort)99);w.Write(client0.ToByteArray());w.Write((byte)15);}),0);
        Assert(Take().Count==0,"protocol mismatch has no capability agreement");
        Guid limitedClient=Guid.NewGuid();Server(Frame(StatisticsPacketType.ImproveGameHello,w=>
        {w.Write(ImproveGameProtocol.Version);w.Write(limitedClient.ToByteArray());w.Write((byte)ImproveGameCapability.Space);}),0);
        var limitedWelcome=Take().Single();Guid limitedServer;
        using(var frame=Body(limitedWelcome.Bytes))
        {frame.ReadUInt16();ImproveGameProtocol.ReadIdentity(frame);limitedServer=ImproveGameProtocol.ReadIdentity(frame);
            Assert((ImproveGameCapability)frame.ReadByte()==ImproveGameCapability.Space,"handshake advertises capability intersection");}
        Server(Frame(StatisticsPacketType.ImproveGameReady,w=>ImproveGameProtocol.WriteSession(w,client0,server0)),0);
        Put(4);Space(0);Assert(Flush().Count==0,"old ready cannot activate a replacement session");
        Server(Frame(StatisticsPacketType.ImproveGameReady,w=>ImproveGameProtocol.WriteSession(w,limitedClient,limitedServer)),0);
        Put(4);var unnegotiated=new SelectorItemOperation{Source=0};On_MessageBuffer.Deliver(0,unnegotiated.Receive);Drain(unnegotiated.Pending);
        Assert(Flush().Count==0,"unnegotiated selector capability remains suppressed");
        Put(4);Space(0);Assert(Flush().Count==1,"negotiated space capability survives other capability rejection");
        int beforeOverflow=Logger.Warnings;
        for(int i=0;i<ImproveGameProtocol.MaxPending/2+3;i++){Put(4);Space(0);}
        Assert(Logger.Warnings==beforeOverflow+1,"queue overflow emits only one diagnostic per session");
        int accepted=0;
        while(true)
        {
            var packets=Flush();if(packets.Count==0)break;
            Assert(packets.Count<=8,"server output is bounded per frame");
            foreach(var packet in packets)
            using(var frame=Body(packet.Bytes))
            {
                Assert(ImproveGameProtocol.TryReadBatch(frame,limitedClient,limitedServer,out _,out var entries),"bounded queue emits valid batches");
                accepted+=entries.Count;
            }
        }
        Assert(accepted==ImproveGameProtocol.MaxPending,"queue never stores more than the configured entry budget");
        Server(Frame(StatisticsPacketType.ImproveGameCreditBatch,w=>w.Write(1)),1);
        Assert(Flush().Count==0,"client uploaded credits ignored");
        ServerMode();network.OnWorldUnload();Drain(second.Pending);Assert(Flush().Count==0,"world reset invalidates old task and queue");
        M4ClientChecks.Run(network);
        network.Unload();tools.Unload();Game.netMode=0;Game.dedServ=false;Game.player=new[]{new Player()};
        ProtocolChecks();
        Console.WriteLine($"M4_CHECKS_OK: {cases} assertions; handshake, actual sender, two players, deferred tasks, server-only results, replay, disconnect, bounds and malformed batches");
    }

    /// <summary>用生产字节编解码测试截断、过量条目、非法指标和游标原子性。</summary>
    private static void ProtocolChecks()
    {
        Guid c=Guid.NewGuid(),s=Guid.NewGuid();var cursor=new ImproveGameCreditCursor();cursor.Reset(c);
        Assert(!cursor.Welcome(Guid.NewGuid(),s,ImproveGameProtocol.All),"old challenge welcome rejected");
        Assert(cursor.Welcome(c,s,ImproveGameProtocol.All),"matching welcome accepted");
        Assert(!cursor.Advance(2)&&cursor.Advance(1)&&!cursor.Advance(1),"cursor rejects gaps and replay");
        Assert(cursor.Welcome(c,s,ImproveGameProtocol.All)&&cursor.Sequence==1,"repeat welcome retains cursor");
        Assert(!cursor.Welcome(c,Guid.NewGuid(),ImproveGameProtocol.All),"same challenge cannot switch server identity");
        byte[] valid=RawBatch(c,s,TileChangeKind.TileRemoved,1);
        for(int n=0;n<valid.Length;n++)
        {
            using var reader=new BinaryReader(new MemoryStream(valid[..n]));
            Assert(!ImproveGameProtocol.TryReadBatch(reader,c,s,out _,out _),"every truncated prefix rejected");
        }
        foreach(var pair in new[]{((TileChangeKind)255,1L),(TileChangeKind.TileRemoved,0L),(TileChangeKind.TileRemoved,-1L),(TileChangeKind.TileRemoved,ImproveGameProtocol.MaxCount+1)})
        {using var reader=new BinaryReader(new MemoryStream(RawBatch(c,s,pair.Item1,pair.Item2)));
            Assert(!ImproveGameProtocol.TryReadBatch(reader,c,s,out _,out _),"invalid result rejected atomically");}
        foreach(int badLength in new[]{-1,513,int.MaxValue})
        {
            using var bad=new MemoryStream();using var w=new BinaryWriter(bad);
            ImproveGameProtocol.WriteSession(w,c,s);w.Write(1UL);w.Write(1UL);w.Write((ushort)1);w.Write((byte)TileChangeKind.TileRemoved);
            w.Write7BitEncodedInt(badLength);bad.Position=0;using var reader=new BinaryReader(bad);
            Assert(!ImproveGameProtocol.TryReadBatch(reader,c,s,out _,out _),"oversized or negative name rejected before allocation");
        }
        using(var bad=new MemoryStream())
        {
            using var w=new BinaryWriter(bad);ImproveGameProtocol.WriteSession(w,c,s);w.Write(1UL);w.Write(1UL);
            w.Write((ushort)(ImproveGameProtocol.MaxEntries+1));bad.Position=0;using var reader=new BinaryReader(bad);
            Assert(!ImproveGameProtocol.TryReadBatch(reader,c,s,out _,out _),"oversized entry count rejected before allocation");
        }
        using(var reader=new BinaryReader(new MemoryStream(valid.Concat(new byte[]{0}).ToArray())))
            Assert(!ImproveGameProtocol.TryReadBatch(reader,c,s,out _,out _),"trailing data rejected within frame");
        var credits=Enumerable.Range(0,ImproveGameProtocol.MaxEntries).Select(i=>new ImproveGameCredit(TileChangeKind.TilePlaced,
            new StableContentKey(new string('m',512),new string('c',512)),0,1)).ToList();
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        ImproveGameProtocol.WriteBatch(writer,c,s,1,1,credits);
        Assert(stream.Length<ImproveGameProtocol.MaxBodyBytes,"longest legal names fit packet byte budget");
        cursor.Reset(Guid.NewGuid());Assert(!cursor.Welcome(c,s,ImproveGameProtocol.All),"world or character change rejects old handshake");
    }

    /// <summary>构造可篡改的原始批次，不借助生产写入器提前过滤非法输入。</summary>
    private static byte[] RawBatch(Guid c,Guid s,TileChangeKind kind,long count)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        ImproveGameProtocol.WriteSession(w,c,s);w.Write(1UL);w.Write(1UL);w.Write((ushort)1);
        w.Write((byte)kind);w.Write("Terraria");w.Write("Tile1");w.Write(0);w.Write(count);return stream.ToArray();
    }
    /// <summary>包装当前协议的显式帧，避免依赖底层共享流的 Length。</summary>
    private static byte[] Frame(StatisticsPacketType type,Action<BinaryWriter> body)
    {using var s=new MemoryStream();using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))body(w);
        using var packet=new MemoryStream();using var output=new BinaryWriter(packet);output.Write((byte)type);output.Write((ushort)s.Length);output.Write(s.ToArray());return packet.ToArray();}
    /// <summary>读取捕获包的有界正文。</summary>
    private static BinaryReader Body(byte[] bytes)
    {using var r=new BinaryReader(new MemoryStream(bytes));r.ReadByte();return new BinaryReader(new MemoryStream(r.ReadBytes(r.ReadUInt16())));}
    /// <summary>以真实连接上下文投递服务器包。</summary>
    private static void Server(byte[] bytes,int sender)
    {ServerMode();On_MessageBuffer.Deliver(sender,()=>Handle(bytes,sender));}
    /// <summary>切换为专用服务器，读取本地角色或 ModPlayer 会立即令测试失败。</summary>
    private static void ServerMode(){Game.netMode=2;Game.dedServ=true;}
    /// <summary>投递到第一名玩家的客户端。</summary>
    private static void Client(byte[] bytes){Game.netMode=1;Game.dedServ=false;Game.myPlayer=0;Handle(bytes,256);}
    /// <summary>执行生产包正文分派。</summary>
    private static void Handle(byte[] bytes,int sender)
    {using var r=new BinaryReader(new MemoryStream(bytes));ImproveGameMultiplayerSystem.HandlePacket((StatisticsPacketType)r.ReadByte(),r,sender);}
    /// <summary>通过真实上游接收方法执行一次空间任务。</summary>
    private static void Space(int sender){ServerMode();On_MessageBuffer.Deliver(sender,new SpaceWandOperation{Source=sender}.Receive);}
    /// <summary>提交一帧服务器聚合结果并读取已发送包。</summary>
    private static List<(int To,byte[] Bytes)> Flush(){ServerMode();network.PostUpdateWorld();return Take();}
    /// <summary>移出捕获队列，防止测试把旧包当作新结果。</summary>
    private static List<(int To,byte[] Bytes)> Take(){var packets=ModPacket.Sent.ToList();ModPacket.Sent.Clear();return packets;}
    /// <summary>填充一个可破坏普通物块。</summary>
    private static void Put(int x){Game.Tiles[x,4].HasTile=true;Game.Tiles[x,4].TileType=1;}
    /// <summary>推进真实迭代器。</summary>
    private static void Drain(IEnumerator iterator){while(iterator.MoveNext())if(iterator.Current is IEnumerator child)Drain(child);}
    /// <summary>记录有含义的行为断言。</summary>
    private static void Assert(bool value,string name){if(!value)throw new Exception("M4: "+name);cases++;}
}
