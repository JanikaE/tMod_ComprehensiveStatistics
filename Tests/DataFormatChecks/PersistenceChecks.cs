using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria.ModLoader.IO;

/// <summary>使用真实 TagIO 验证压缩保存重载、缺失模组数据及角色双范围写入。</summary>
internal static class PersistenceChecks
{
    private static readonly BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static Type serializer;
    private static int checks;

    /// <summary>旧版和当前版各执行两次真实压缩读写，全程只使用内存中的构造标签。</summary>
    internal static void Run(Assembly assembly)
    {
        serializer=assembly.GetType("ComprehensiveStatistics.Common.Statistics.StatisticsDataSerializer");
        for(int version=1;version<=4;version++)
        {
            Guid a=Guid.NewGuid(),b=Guid.NewGuid();
            var root=new TagCompound
            {
                ["FormatVersion"]=version,["Career"]=Scope(version,17,1234567L),
                ["WorldSlices"]=new List<TagCompound>{new(){["WorldId"]=a.ToString("N"),["Data"]=Scope(version,17,1234500L)},
                    new(){["WorldId"]=b.ToString("N"),["Data"]=Scope(version,31,1234600L)}},
                ["ExpansionStates"]=new List<TagCompound>{new(){["Tab"]="blocks",["GroupPath"]="tiles",["Expanded"]=true},
                    new(){["Tab"]="blocks",["GroupPath"]="tiles",["Expanded"]=false,["WorldId"]=a.ToString("N")},
                    new(){["Tab"]="blocks",["GroupPath"]="tiles",["Expanded"]=true,["WorldId"]=b.ToString("N")}}
            };
            object loaded=Read(root);var saved=Save(loaded);
            Check(saved.GetList<TagCompound>("ListViewStates").Count==0,"legacy save defaults to no view records");
            root["ListViewStates"]=new List<TagCompound>
            {
                View("blocks","Units","Descending",340f),
                View("blocks","Name","Ascending",68f,a),
                View("blocks","Events","None",102f,b),
                View("items","Amount","Descending",170f),
                View("bad","Name","None",55f,invalidWorld:true),
                View("negative","Name","None",-1f),
                View("nonfinite","Name","None",float.NaN)
            };
            loaded=Read(root);saved=Save(loaded);
            var views=saved.GetList<TagCompound>("ListViewStates");
            Check(views.Count==6,"malformed view isolated without losing valid records");
            Check(views.Single(t=>t.GetString("Tab")=="blocks"&&!t.ContainsKey("WorldId")).GetFloat("ScrollPosition")==340f,
                "career view isolated");
            Check(views.Single(t=>t.GetString("Tab")=="blocks"&&t.ContainsKey("WorldId")&&t.GetString("WorldId")==a.ToString("N")).GetString("SortDirection")=="Ascending",
                "world A sort isolated");
            Check(views.Single(t=>t.GetString("Tab")=="blocks"&&t.ContainsKey("WorldId")&&t.GetString("WorldId")==b.ToString("N")).GetString("SortDirection")=="None",
                "world B retains explicit no sort");
            Check(views.Single(t=>t.GetString("Tab")=="items").GetFloat("ScrollPosition")==170f,"other tab isolated");
            Check(views.Where(t=>t.GetString("Tab")=="negative"||t.GetString("Tab")=="nonfinite").All(t=>t.GetFloat("ScrollPosition")==0f),
                "invalid scroll defaults to top");
            Check(saved.GetInt("FormatVersion")==4,"current root format");
            Check(!saved.Select(pair=>pair.Key).Except(new[]{"FormatVersion","Career","WorldSlices","ExpansionStates","ListViewStates"}).Any(),"runtime compatibility state is not saved");
            Check(saved.GetCompound("Career").GetLong("StartedAtUnixMilliseconds")==1234567L
                &&World(saved,a).GetLong("StartedAtUnixMilliseconds")==1234500L&&World(saved,b).GetLong("StartedAtUnixMilliseconds")==1234600L,"original start times");
            Check(saved.GetList<TagCompound>("ExpansionStates").Count==3
                &&!saved.GetList<TagCompound>("ExpansionStates").Single(t=>t.ContainsKey("WorldId")&&t.GetString("WorldId")==a.ToString("N")).GetBool("Expanded"),"separate career/world folds");
            Validate(saved.GetCompound("Career"),17);Validate(World(saved,a),17);Validate(World(saved,b),31);
            string original=Signature(saved);
            for(int cycle=0;cycle<2;cycle++)
            {
                loaded=Read(RoundTrip(new TagCompound{["CharacterStatistics"]=saved}).GetCompound("CharacterStatistics"));saved=Save(loaded);
                Check(Signature(saved)==original,"compressed character save/reload preserves all fields");
            }
            Add(assembly,loaded,a);saved=Save(loaded);
            Check(EntryOf(saved.GetCompound("Career"),"blocks/walls-placed","Terraria","Stone").GetLong("Events")==5
                &&EntryOf(World(saved,a),"blocks/walls-placed","Terraria","Stone").GetLong("Events")==5,"new event enters career and current world");
            Check(!World(saved,b).GetList<TagCompound>("Entries").Any(t=>t.GetString("StatisticId")=="blocks/walls-placed"),"other world slice unchanged");
            Check(saved.GetCompound("Career").GetLong("StartedAtUnixMilliseconds")==1234567L
                &&World(saved,a).GetLong("StartedAtUnixMilliseconds")==1234500L,"new activity does not reset start times");
            Check(Signature(Save(Read(RoundTrip(new TagCompound{["CharacterStatistics"]=saved}).GetCompound("CharacterStatistics"))))==Signature(saved),"new activity survives reload");
            var worldInput=new object[]{RoundTrip(new TagCompound{["WorldStatistics"]=Scope(version,17,1234567L)}),null,null};
            Check((bool)serializer.GetMethod("TryDeserializeWorld",Flags).Invoke(null,worldInput),"world scope loads");
            var world=(TagCompound)serializer.GetMethod("SerializeWorld",Flags).Invoke(null,new[]{worldInput[1]});Validate(world,17);
            worldInput=new object[]{RoundTrip(new TagCompound{["WorldStatistics"]=world}),null,null};
            Check((bool)serializer.GetMethod("TryDeserializeWorld",Flags).Invoke(null,worldInput),"world scope reloads");
            Check(Signature(serializer.GetMethod("SerializeWorld",Flags).Invoke(null,new[]{worldInput[1]}))==Signature(world),"world scope round trip");
        }
        Console.WriteLine($"PERSISTENCE_CHECKS_OK: {checks} assertions; versions 1/2/3/4, real compressed TagIO, dual scopes, isolated worlds, missing mod content, object styles, wire Events/Units, timestamps, folds and list views");
    }

    /// <summary>构造独立列表视图及损坏世界标识，验证生产加载器的容错与隔离。</summary>
    private static TagCompound View(string tab,string column,string direction,float position,Guid? world=null,bool invalidWorld=false)
    {
        var tag=new TagCompound{["Tab"]=tab,["SortColumn"]=column,["SortDirection"]=direction,["ScrollPosition"]=position};
        if(world.HasValue)tag["WorldId"]=world.Value.ToString("N");
        if(invalidWorld)tag["WorldId"]="invalid";
        return tag;
    }

    /// <summary>提供不同世界的旧累计；旧拆除数不能由放置数倒推。</summary>
    private static TagCompound Scope(int version,long placed,long started)=>new()
    {
        ["FormatVersion"]=version,["StartedAtUnixMilliseconds"]=started,
        ["Entries"]=new List<TagCompound>{Entry("blocks/tiles-placed","Terraria","Dirt",placed),Entry("blocks/tiles-mined","Terraria","Dirt",9),
            Entry("blocks/tile-objects-placed","RemovedProvider","Desk",2,style:"7"),Entry("blocks/wiring-components","Terraria","WireRed",8,3)}
    };

    /// <summary>创建稳定内容、样式及各指标相互独立的原始条目。</summary>
    private static TagCompound Entry(string statistic,string mod,string name,long events,long units=0,string style="")=>new()
    { ["StatisticId"]=statistic,["ContentMod"]=mod,["ContentName"]=name,["Style"]=style,["Events"]=events,["Units"]=units,["Amount"]=0L };

    /// <summary>使用游戏自带的真实压缩序列化器读写内存标签，不接触用户文件。</summary>
    private static TagCompound RoundTrip(TagCompound tag)
    {
        using var stream=new MemoryStream();TagIO.ToStream(tag,stream,true);
        using var input=new MemoryStream(stream.ToArray());return TagIO.FromStream(input,true);
    }

    /// <summary>调用生产加载器并保留具体错误。</summary>
    private static object Read(TagCompound root)
    {
        object[] input={new TagCompound{["CharacterStatistics"]=root},null,null};
        Check((bool)serializer.GetMethod("TryDeserialize",Flags).Invoke(null,input),"character loads: "+input[2]);return input[1];
    }

    /// <summary>调用生产保存器。</summary>
    private static TagCompound Save(object data)=>(TagCompound)serializer.GetMethod("Serialize",Flags).Invoke(null,new[]{data});

    /// <summary>向生产统一累计入口提交一个新事件，验证双范围路由。</summary>
    private static void Add(Assembly assembly,object data,Guid world)
    {
        object content=Activator.CreateInstance(assembly.GetType("ComprehensiveStatistics.Common.Statistics.StableContentKey"),Flags,null,new object[]{"Terraria","Stone"},null);
        object key=assembly.GetType("ComprehensiveStatistics.Common.Statistics.StatisticKeys").GetMethod("GetPlacedWallKey",Flags).Invoke(null,new[]{content});
        object metrics=Activator.CreateInstance(assembly.GetType("ComprehensiveStatistics.Common.Statistics.StatisticMetrics"),Flags,null,new object[]{5L,0L,0L},null);
        data.GetType().GetMethod("Add",Flags).Invoke(data,new[]{(object)world,key,metrics,DateTimeOffset.UtcNow});
    }

    /// <summary>按 UUID 查找切片，不依赖保存顺序。</summary>
    private static TagCompound World(TagCompound root,Guid id)=>root.GetList<TagCompound>("WorldSlices").Single(t=>t.GetString("WorldId")==id.ToString("N")).GetCompound("Data");

    /// <summary>按实际统计及稳定内容键查找条目。</summary>
    private static TagCompound EntryOf(TagCompound scope,string statistic,string mod,string name)=>scope.GetList<TagCompound>("Entries")
        .Single(t=>t.GetString("StatisticId")==statistic&&t.GetString("ContentMod")==mod&&t.GetString("ContentName")==name);

    /// <summary>保留缺失内容、样式与组件字段，不进行历史补算。</summary>
    private static void Validate(TagCompound scope,long placed)
    {
        Check(scope.GetInt("FormatVersion")==4,"scope current format");
        Check(!scope.GetList<TagCompound>("Entries").Any(tag=>tag.GetString("StatisticId").StartsWith("blocks/liquids-",StringComparison.Ordinal)),
            "legacy totals do not create liquid history");
        Check(EntryOf(scope,"blocks/tiles-placed","Terraria","Dirt").GetLong("Events")==placed
            &&EntryOf(scope,"blocks/tiles-mined","Terraria","Dirt").GetLong("Events")==9,"old totals retained without backfill");
        var missing=EntryOf(scope,"blocks/tile-objects-placed","RemovedProvider","Desk");
        Check(missing.GetString("Style")=="7"&&missing.GetLong("Events")==2,"missing mod content and furniture style");
        var wire=EntryOf(scope,"blocks/wiring-components","Terraria","WireRed");
        Check(wire.GetLong("Events")==8&&wire.GetLong("Units")==3,"wire placement/removal fields retained");
    }

    /// <summary>比较语义数据，不依赖压缩字节或字典的枚举顺序。</summary>
    private static string Signature(object value)
    {
        if(value is TagCompound tag)return "{"+string.Join(";",tag.OrderBy(p=>p.Key).Select(p=>p.Key+":"+Signature(p.Value)))+"}";
        if(value is string text)return text;
        if(value is IEnumerable list)return "["+string.Join(";",list.Cast<object>().Select(Signature))+"]";
        return value is IFormattable number?number.ToString(null,CultureInfo.InvariantCulture):value?.ToString()??"null";
    }

    /// <summary>检查一个存档不变量。</summary>
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
}
