using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
/// <summary>使用生产序列化器验证旧格式的数据保留及版本升级。</summary>
internal static class FormatChecks
{
    private static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance;
    /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
    private static Terraria.ModLoader.IO.TagCompound Scope(int version=1)
    {
        return new Terraria.ModLoader.IO.TagCompound {
            ["FormatVersion"]=version,["StartedAtUnixMilliseconds"]=1234567L,
            ["Entries"]=new List<Terraria.ModLoader.IO.TagCompound> { new() {
                ["StatisticId"]="blocks/tiles-placed",["ContentMod"]="Terraria",["ContentName"]="Dirt",
                ["Style"]="",["Events"]=17L,["Units"]=0L,["Amount"]=0L } }
        };
    }
    /// <summary>提供隔离回归中的模拟入口或数据验证。</summary>
    public static void Run(Assembly assembly)
    {
        Type tags=typeof(Terraria.ModLoader.IO.TagCompound).Assembly.GetType("Terraria.ModLoader.IO.TagSerializer");
        tags.GetMethod("AddSerializer",Flags).Invoke(null,new[]{Activator.CreateInstance(typeof(Terraria.ModLoader.IO.TagCompound).Assembly.GetType("Terraria.ModLoader.IO.BoolTagSerializer"),true)});
        Type serializer=assembly.GetType("ComprehensiveStatistics.Common.Statistics.StatisticsDataSerializer");
        for(int version=1;version<=3;version++)
        {
        var root=new Terraria.ModLoader.IO.TagCompound {
            ["FormatVersion"]=version,["Career"]=Scope(version),
            ["WorldSlices"]=new List<Terraria.ModLoader.IO.TagCompound> { new() {
                ["WorldId"]=Guid.NewGuid().ToString("N"),["Data"]=Scope(version) } },
            ["ExpansionStates"]=new List<Terraria.ModLoader.IO.TagCompound> { new() {
                ["Tab"]="blocks",["GroupPath"]="tiles",["Expanded"]=true } }
        };
        var player=new Terraria.ModLoader.IO.TagCompound { ["CharacterStatistics"]=root };
        object[] input={player,null,null};
        if(!(bool)serializer.GetMethod("TryDeserialize",Flags).Invoke(null,input))
            throw new Exception("Version 1 character load failed: "+input[2]);
        var saved=(Terraria.ModLoader.IO.TagCompound)serializer.GetMethod("Serialize",Flags).Invoke(null,new[]{input[1]});
        var career=saved.GetCompound("Career");
        var world=saved.GetList<Terraria.ModLoader.IO.TagCompound>("WorldSlices")[0].GetCompound("Data");
        if(saved.GetInt("FormatVersion")!=4 || career.GetInt("FormatVersion")!=4 || world.GetInt("FormatVersion")!=4
            || career.GetLong("StartedAtUnixMilliseconds")!=1234567L || world.GetLong("StartedAtUnixMilliseconds")!=1234567L
            || career.GetList<Terraria.ModLoader.IO.TagCompound>("Entries")[0].GetLong("Events")!=17L
            || world.GetList<Terraria.ModLoader.IO.TagCompound>("Entries")[0].GetLong("Events")!=17L
            || saved.GetList<Terraria.ModLoader.IO.TagCompound>("ExpansionStates").Count!=1)
            throw new Exception("Upgrade lost version, data, timestamp or fold state.");
        foreach(int invalidVersion in new[]{-1,0,5,99})
        {
            root["FormatVersion"]=invalidVersion;
            if((bool)serializer.GetMethod("TryDeserialize",Flags).Invoke(null,new object[]{player,null,null}))
                throw new Exception("Unsupported character format accepted: "+invalidVersion);
            var badWorld=new object[]{new Terraria.ModLoader.IO.TagCompound{["WorldStatistics"]=Scope(invalidVersion)},null,null};
            if((bool)serializer.GetMethod("TryDeserializeWorld",Flags).Invoke(null,badWorld))
                throw new Exception("Unsupported world format accepted: "+invalidVersion);
            using var badStream=new MemoryStream();
            using(var badWriter=new BinaryWriter(badStream,System.Text.Encoding.UTF8,true)) badWriter.Write(invalidVersion);
            badStream.Position=0;
            using var badReader=new BinaryReader(badStream);
            if((bool)serializer.GetMethod("TryReadScope",Flags).Invoke(null,new object[]{badReader,null,null}))
                throw new Exception("Unsupported network format accepted: "+invalidVersion);
        }
        var worldInput=new object[]{new Terraria.ModLoader.IO.TagCompound{["WorldStatistics"]=Scope(version)},null,null};
        if(!(bool)serializer.GetMethod("TryDeserializeWorld",Flags).Invoke(null,worldInput)) throw new Exception("Version 1 world failed.");
        var upgradedWorld=(Terraria.ModLoader.IO.TagCompound)serializer.GetMethod("SerializeWorld",Flags).Invoke(null,new[]{worldInput[1]});
        if(upgradedWorld.GetInt("FormatVersion")!=4 || upgradedWorld.GetList<Terraria.ModLoader.IO.TagCompound>("Entries")[0].GetLong("Events")!=17L)
            throw new Exception("World data was not preserved.");
        using var stream=new MemoryStream();
        using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)) { writer.Write(version); writer.Write(1234567L); writer.Write(0); }
        stream.Position=0;
        using var reader=new BinaryReader(stream);
        object[] network={reader,null,null};
        if(!(bool)serializer.GetMethod("TryReadScope",Flags).Invoke(null,network)
            || (int)network[1].GetType().GetProperty("FormatVersion",Flags).GetValue(network[1])!=4)
            throw new Exception("Legacy network scope failed.");
        }
        Console.WriteLine("FORMAT_UPGRADE_CHECKS_OK: versions 1/2/3 to 4, career, world slices, world data, start times, fold state, network read and unsupported-version rejection");
        PersistenceChecks.Run(assembly);
        LiquidDataChecks.Run(assembly);
        SubTabChecks.Run(assembly);
    }
}




