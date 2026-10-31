using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Terraria.ModLoader.IO;

/// <summary>加载生产程序集验证子页迁移、独立视图、液体显示精度和响应式按钮布局。</summary>
internal static class SubTabChecks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Type serializer;
    private static int checks;

    /// <summary>只使用构造标签和内存压缩，不读取或修改真实人物世界。</summary>
    internal static void Run(Assembly assembly)
    {
        serializer = assembly.GetType("ComprehensiveStatistics.Common.Statistics.StatisticsDataSerializer");
        MigrationChecks();
        var definition = assembly.GetType("ComprehensiveStatistics.Common.Statistics.StatisticsSubTabs");
        Check((string)definition.GetMethod("Resolve", Flags).Invoke(null, new object[]{"Blocks", "invalid"}) == "Tiles", "unknown subpage defaults");
        Check(((IEnumerable)definition.GetMethod("For", Flags).Invoke(null, new object[]{"Items"})).Cast<object>().Count() == 0, "ordinary page has no subtab strip");
        FishingChecks(assembly);
        DisplayChecks(assembly);
        ProjectionChecks(assembly);
        LayoutChecks(assembly);
        Console.WriteLine($"SUBTAB_CHECKS_OK: {checks} assertions; legacy 1/2/3/4 migration, independent pages/worlds, selected page, compressed reload, exact liquid display and wrapped button layout");
    }

    /// <summary>验证钓鱼子页身份、旧鱼饵完整迁移、旧液体折叠及鱼竿结果筛选合计。</summary>
    private static void FishingChecks(Assembly assembly)
    {
        var definition = assembly.GetType("ComprehensiveStatistics.Common.Statistics.StatisticsSubTabs");
        var pages = ((IEnumerable)definition.GetMethod("For", Flags).Invoke(null, new object[]{"Fishing"})).Cast<object>().ToArray();
        Check(pages.Select(p => (string)Get(p,"Id")).SequenceEqual(new[]{"Catches","Casts","Bait"}), "fishing subpage order");
        Check((string)definition.GetMethod("Resolve",Flags).Invoke(null,new object[]{"Fishing","unknown"}) == "Catches", "fishing default is catches");
        string world = Guid.NewGuid().ToString("N");
        var root = new TagCompound{["FormatVersion"]=4,["Career"]=Scope(4),["WorldSlices"]=new List<TagCompound>(),
            ["ListViewStates"]=new List<TagCompound>(),["ExpansionStates"]=new List<TagCompound>(),
            ["SelectedSubTabs"]=new List<TagCompound>{new(){["Tab"]="Fishing",["SubTab"]="Casts"}}};
        foreach (string context in new[]{null,world})
        {
            root.GetList<TagCompound>("ListViewStates").Add(View("Fishing","Units","Descending",123f,context));
            root.GetList<TagCompound>("ListViewStates").Add(View("Bait","Rate","Ascending",67f,context));
            root.GetList<TagCompound>("ExpansionStates").Add(Fold("Fishing","\u001fMods.ComprehensiveStatistics.UI.Fishing.Liquids.Water",false,context));
            root.GetList<TagCompound>("ExpansionStates").Add(Fold("Fishing","\u001fMods.ComprehensiveStatistics.UI.Fishing.CastsGroup",false,context));
            root.GetList<TagCompound>("ExpansionStates").Add(Fold("Bait","\u001fMods.ComprehensiveStatistics.UI.Bait.UsageGroup",false,context));
        }
        var saved = Save(Read(root));
        foreach (string context in new[]{null,world})
        {
            Check(saved.GetList<TagCompound>("ListViewStates").Single(t=>Match(t,"Fishing/Bait",context)).GetFloat("ScrollPosition")==67f,"bait scroll migrated in each scope");
            Check(saved.GetList<TagCompound>("ListViewStates").Single(t=>Match(t,"Fishing/Catches",context)).GetFloat("ScrollPosition")==0f,"catch scroll reset after split");
            Check(saved.GetList<TagCompound>("ExpansionStates").Count(t=>Match(t,"Fishing/Catches",context))==1,"only liquid folds move into catches");
            Check(saved.GetList<TagCompound>("ExpansionStates").Count(t=>Match(t,"Fishing/Bait",context))==1,"bait fold moves intact");
        }
        saved.GetList<TagCompound>("ListViewStates").Single(t=>Match(t,"Fishing/Bait",null))["ScrollPosition"]=91f;
        saved = Save(Read(Compressed(saved)));
        Check(saved.GetList<TagCompound>("ListViewStates").Single(t=>Match(t,"Fishing/Bait",null)).GetFloat("ScrollPosition")==91f,"existing fishing page wins over legacy state");
        Check(saved.GetList<TagCompound>("SelectedSubTabs").Single().GetString("SubTab")=="Casts","fishing selection survives compressed reload");
        var scope = root.GetCompound("Career");
        string[] ids={"fishing/results/successful","fishing/results/empty-reels","fishing/results/interrupted"};
        foreach (var id in ids)
        {
            scope.GetList<TagCompound>("Entries").Add(new(){["StatisticId"]=id,["Events"]=10L});
            scope.GetList<TagCompound>("Entries").Add(new(){["StatisticId"]=id,["ContentMod"]="Terraria",["ContentName"]="WoodFishingPole",["Events"]=2L});
            scope.GetList<TagCompound>("Entries").Add(new(){["StatisticId"]=id,["ContentMod"]="MissingMod",["ContentName"]="Rod",["Events"]=3L});
        }
        object model=Read(root); object data=Get(model,"Career");
        var method=assembly.GetType("ComprehensiveStatistics.Common.UI.Elements.FishingCastStatisticsList").GetMethod("CreateGroups",Flags);
        var resolverType=method.GetParameters()[1].ParameterType;
        var tupleType=resolverType.GetMethod("Invoke").ReturnType;
        var parameter=System.Linq.Expressions.Expression.Parameter(resolverType.GenericTypeArguments[0]);
        var tuple=System.Linq.Expressions.Expression.New(tupleType.GetConstructors().Single(),
            System.Linq.Expressions.Expression.Constant("Test rod"),System.Linq.Expressions.Expression.Constant(0),
            System.Linq.Expressions.Expression.Constant(null,tupleType.GenericTypeArguments[2]));
        var resolver=System.Linq.Expressions.Expression.Lambda(resolverType,tuple,parameter).Compile();
        object[] groups=((IEnumerable)method.Invoke(null,new[]{data,resolver})).Cast<object>().ToArray();
        Check(groups.Length==3 && groups.Sum(g=>(long)Get(g,"TotalEvents"))==30L,"known rods plus history total without double count");
        Check(groups.All(g=>((IEnumerable)Get(g,"Items")).Cast<object>().Count()==3),"three outcome leaves per rod");
        Check((long)Get(groups.Single(g=>(string)Get(g,"StateKey")=="fishing/rod/unknown"),"TotalEvents")==15L,"unknown history is aggregate minus rod details");
        var listType=assembly.GetType("ComprehensiveStatistics.Common.UI.Elements.VirtualizedStatisticsList");
        object list=RuntimeHelpers.GetUninitializedObject(listType);
        listType.GetField("searchQuery",Flags).SetValue(list,string.Empty);
        var sourceField=listType.GetField("sourceFilter",Flags);
        sourceField.SetValue(list,Enum.Parse(sourceField.FieldType,"Modded"));
        var filtered=groups.Select(g=>listType.GetMethod("BuildFilteredGroup",Flags).Invoke(list,new[]{g,(object)false})).Where(g=>g!=null).ToArray();
        Check(filtered.Sum(g=>(long)Get(g,"TotalEvents"))==24L,"modded filter follows rod source while history remains neutral");
        sourceField.SetValue(list,Enum.Parse(sourceField.FieldType,"Vanilla"));
        filtered=groups.Select(g=>listType.GetMethod("BuildFilteredGroup",Flags).Invoke(list,new[]{g,(object)false})).Where(g=>g!=null).ToArray();
        Check(filtered.Sum(g=>(long)Get(g,"TotalEvents"))==21L,"vanilla filter follows rod source while history remains neutral");
        sourceField.SetValue(list,Enum.Parse(sourceField.FieldType,"All"));
        listType.GetField("searchQuery",Flags).SetValue(list,"Test rod");
        filtered=groups.Select(g=>listType.GetMethod("BuildFilteredGroup",Flags).Invoke(list,new[]{g,(object)false})).Where(g=>g!=null).ToArray();
        Check(filtered.Sum(g=>(long)Get(g,"TotalEvents"))==15L,"rod name search includes its outcome leaves");
        var loaded=Save(Read(Compressed(Save(model))));
        Check(loaded.GetCompound("Career").GetList<TagCompound>("Entries").Count==9,"rod details persist beside old aggregate keys");
        Check(!((IEnumerable)method.Invoke(null,new object[]{null,resolver})).Cast<object>().Any(),"empty fishing data creates no rod rows");
    }

    /// <summary>直接执行生产液体投影及过滤，确认四类型顺序、双列原始合计和来源语义。</summary>
    private static void ProjectionChecks(Assembly assembly)
    {
        var scope = Scope(4);
        var entries = scope.GetList<TagCompound>("Entries");
        string[] names = {"Water","Lava","Honey","Shimmer"};
        for (int i=0;i<4;i++)
        {
            entries.Add(new TagCompound{["StatisticId"]="blocks/liquids-placed",["ContentMod"]="Terraria",["ContentName"]="Liquid"+names[i],["Units"]=(long)(i+1)});
            entries.Add(new TagCompound{["StatisticId"]="blocks/liquids-removed",["ContentMod"]="Terraria",["ContentName"]="Liquid"+names[i],["Units"]=(long)(i+1)*80});
        }
        object model = Read(new TagCompound{["FormatVersion"]=4,["Career"]=scope,["WorldSlices"]=new List<TagCompound>()});
        object data = model.GetType().GetProperty("Career",Flags).GetValue(model);
        var projection = assembly.GetType("ComprehensiveStatistics.Common.UI.Elements.LiquidStatisticsList");
        object group = ((IEnumerable)projection.GetMethod("CreateGroups",Flags).Invoke(null,new[]{data,null})).Cast<object>().Single();
        Check((long)Get(group,"TotalEvents")==10 && (long)Get(group,"TotalUnits")==800,"display projection retains separate raw volume sums");
        object[] items=((IEnumerable)Get(group,"Items")).Cast<object>().ToArray();
        Check(items.Length==4 && items.Select(t=>(int)Get(t,"VanillaContentId")).SequenceEqual(new[]{0,1,2,3}),"default liquid order uses semantic type, not bucket item id");
        Check(items.All(t=>Get(t,"Source").ToString()=="Vanilla"),"wand-created standard liquid remains vanilla content");
        var listType=assembly.GetType("ComprehensiveStatistics.Common.UI.Elements.VirtualizedStatisticsList");
        object list=RuntimeHelpers.GetUninitializedObject(listType);
        listType.GetField("searchQuery",Flags).SetValue(list,string.Empty);
        object Filter()=>listType.GetMethod("BuildFilteredGroup",Flags).Invoke(list,new[]{group,(object)false});
        object filtered=Filter();
        Check((long)Get(filtered,"TotalEvents")==10 && (long)Get(filtered,"TotalUnits")==800,"all-filter total uses raw integers before formatting");
        var sourceField=listType.GetField("sourceFilter",Flags);
        sourceField.SetValue(list,Enum.Parse(sourceField.FieldType,"Modded"));
        Check(Filter()==null,"mod-only filter yields liquid empty state");
        sourceField.SetValue(list,Enum.Parse(sourceField.FieldType,"Vanilla"));
        listType.GetField("searchQuery",Flags).SetValue(list,"Honey");
        filtered=Filter();
        Check((long)Get(filtered,"TotalEvents")==3 && (long)Get(filtered,"TotalUnits")==240,"search total includes only matching liquid");
        var column=listType.GetField("sortColumn",Flags);var direction=listType.GetField("sortDirection",Flags);
        column.SetValue(list,Enum.Parse(column.FieldType,"Events"));direction.SetValue(list,Enum.Parse(direction.FieldType,"Descending"));
        object[] compare={"A",long.MaxValue,0L,0L,0L,0L,"B",long.MaxValue-1,0L,0L,0L,0L};
        Check((int)listType.GetMethod("CompareRows",Flags).Invoke(list,compare)<0,"liquid sorting compares raw longs without float precision loss");
        object empty=((IEnumerable)projection.GetMethod("CreateGroups",Flags).Invoke(null,new object[]{null,null})).Cast<object>().Single();
        Check(!((IEnumerable)Get(empty,"Items")).Cast<object>().Any(),"no invented zero-valued liquid rows");
    }

    /// <summary>取得生产展示模型属性。</summary>
    private static object Get(object model,string property)=>model.GetType().GetProperty(property,Flags).GetValue(model);

    /// <summary>旧根分组和排序迁移到三页，液体默认空视图，新状态和角色选择跨保存保持。</summary>
    private static void MigrationChecks()
    {
        string[] ids = {"Tiles", "Walls", "Wiring"};
        string[] roots = {"SolidTilesGroup", "BackgroundWallsGroup", "WiringComponentsGroup"};
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        string[] contexts = {null, a.ToString("N"), b.ToString("N")};
        for (int version = 1; version <= 4; version++)
        {
            var folds = new List<TagCompound>(); var views = new List<TagCompound>();
            foreach (string world in contexts)
            {
                views.Add(View("Blocks", "Events", "Descending", 340f, world));
                for (int i = 0; i < 3; i++)
                {
                    string path = "\u001fMods.ComprehensiveStatistics.UI.Blocks." + roots[i];
                    folds.Add(Fold("Blocks", path, i == 1, world));
                    folds.Add(Fold("Blocks", path + "\u001fchild", false, world));
                }
            }
            var root = new TagCompound { ["FormatVersion"] = version,
                ["Career"] = Scope(version), ["WorldSlices"] = new List<TagCompound>(),
                ["ExpansionStates"] = folds, ["ListViewStates"] = views };
            var saved = Save(Read(root));
            Check(!saved.ContainsKey("SelectedSubTabs"), "old save has no invented selection");
            foreach (string world in contexts)
            {
                for (int i = 0; i < 3; i++)
                {
                    var view = saved.GetList<TagCompound>("ListViewStates").Single(t => Match(t, "Blocks/" + ids[i], world));
                    Check(view.GetString("SortColumn") == "Events" && view.GetString("SortDirection") == "Descending"
                        && view.GetFloat("ScrollPosition") == 0f, "legacy sort copied but long-list scroll reset");
                    var migrated = saved.GetList<TagCompound>("ExpansionStates").Where(t => Match(t, "Blocks/" + ids[i], world)).ToArray();
                    Check(migrated.Length == 2 && migrated.All(t => t.GetString("GroupPath").StartsWith("\u001fMods.ComprehensiveStatistics.UI.Blocks." + roots[i]))
                        && migrated[0].GetBool("Expanded") == (i == 1), "only own root and descendants migrated");
                }
            }
            Check(!saved.GetList<TagCompound>("ListViewStates").Any(t => t.GetString("Tab") == "Blocks/Liquids"), "liquid defaults to unsorted top");
            Check(saved.GetCompound("Career").GetLong("StartedAtUnixMilliseconds") == 1234567L, "timestamp retained");
            var allViews = saved.GetList<TagCompound>("ListViewStates");
            allViews.Single(t => Match(t, "Blocks/Tiles", null))["ScrollPosition"] = 102f;
            allViews.Single(t => Match(t, "Blocks/Walls", a.ToString("N")))["SortDirection"] = "Ascending";
            allViews.Add(View("Blocks/Liquids", "Units", "Descending", 34f, b.ToString("N")));
            saved["SelectedSubTabs"] = new List<TagCompound>{new(){["Tab"]="Blocks",["SubTab"]="Liquids"}};
            for (int cycle = 0; cycle < 2; cycle++)
            {
                saved = Save(Read(Compressed(saved)));
                Check(saved.GetList<TagCompound>("ListViewStates").Single(t => Match(t, "Blocks/Tiles", null)).GetFloat("ScrollPosition") == 102f, "new page scroll not overwritten by old Blocks");
                Check(saved.GetList<TagCompound>("ListViewStates").Single(t => Match(t, "Blocks/Walls", a.ToString("N"))).GetString("SortDirection") == "Ascending", "world-specific page sort preserved");
                Check(saved.GetList<TagCompound>("ListViewStates").Single(t => Match(t, "Blocks/Liquids", b.ToString("N"))).GetFloat("ScrollPosition") == 34f, "new liquid view survives migration");
                Check(saved.GetList<TagCompound>("SelectedSubTabs").Single().GetString("SubTab") == "Liquids", "character subpage selection compressed reload");
                Check(saved.GetInt("FormatVersion") == 4, "optional UI fields do not raise format");
            }
            var model = Read(saved);
            object store = model.GetType().GetProperty("ListViewStates", Flags).GetValue(model);
            store.GetType().GetMethod("ResetScrollPositions", Flags).Invoke(store, null);
            var reset = Save(model).GetList<TagCompound>("ListViewStates");
            Check(reset.All(t => t.GetFloat("ScrollPosition") == 0f), "filter changes reset all page/context scrolls");
            Check(reset.Single(t => Match(t, "Blocks/Walls", a.ToString("N"))).GetString("SortDirection") == "Ascending", "filter reset preserves sort");
            saved["SelectedSubTabs"] = new List<TagCompound>{new(){["Tab"]="Blocks",["SubTab"]="unknown"},new(){["Tab"]="Unknown",["SubTab"]="Liquids"}};
            Check(Save(Read(saved)).GetList<TagCompound>("SelectedSubTabs").Single().GetString("SubTab") == "Tiles", "unknown ids isolated and defaulted");
            saved["SelectedSubTabs"] = "broken";
            Check(!Save(Read(saved)).ContainsKey("SelectedSubTabs"), "broken optional selection does not lose data");
        }
    }

    /// <summary>验证小体积与最大整数的显示，不用浮点舍入参与累计或排序。</summary>
    private static void DisplayChecks(Assembly assembly)
    {
        var format = assembly.GetType("ComprehensiveStatistics.Common.Statistics.LiquidVolumeDisplay").GetMethod("Format", Flags);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string locale in new[]{"zh-CN", "en-US", "de-DE"})
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                string Text(long raw) => (string)format.Invoke(null, new object[]{raw});
                Check(Text(255) == "1" && Text(1530) == "6" && Text(0) == "0", "full tiles and zero omit decimal suffix");
                Check(decimal.Parse(Text(155)) == 0.608m && decimal.Parse(Text(80)) == 0.314m, "partial tiles rounded only at display");
                for (long raw = 1; raw < 255; raw++)
                    Check(decimal.Parse(Text(raw)) > 0m, "every sub-tile volume remains nonzero");
                Check(decimal.Parse(Text(long.MaxValue)) == Math.Round(long.MaxValue / 255m, 3), "maximum integer retains decimal precision");
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>对窄画布、中英文宽度和多行布局检查边界、间隔及列表让位所需高度。</summary>
    private static void LayoutChecks(Assembly assembly)
    {
        var layout = assembly.GetType("ComprehensiveStatistics.Common.UI.Elements.StatisticsSubTabLayout").GetMethod("Calculate", Flags);
        foreach (float width in new[]{1f, 60f, 140f, 250f, 420f, 700f})
        foreach (float[] labels in new[]{new[]{32f,48f,32f,32f},new[]{42f,95f,53f,49f}})
        {
            var slots = ((IEnumerable)layout.Invoke(null, new object[]{width,labels})).Cast<object>().ToArray();
            float lastY = -34f, lastRight = 0f;
            foreach (object slot in slots)
            {
                float x = Value(slot,"X"), y = Value(slot,"Y"), w = Value(slot,"Width");
                Check(x >= 0f && x + w <= width && w > 0f, "button stays inside logical width");
                Check(y == lastY ? x >= lastRight + 6f : y >= lastY + 34f && x == 0f, "buttons never overlap and rows keep gap");
                lastY = y; lastRight = x+w;
            }
            if (width == 700f) Check(lastY == 0f, "wide strip occupies one row");
            if (width == 60f) Check(lastY == 102f, "narrow strip pushes list below four rows");
        }
    }

    /// <summary>读取槽位的逻辑像素值。</summary>
    private static float Value(object slot,string name) => (float)slot.GetType().GetProperty(name,Flags).GetValue(slot);
    /// <summary>构造不含统计条目的旧范围。</summary>
    private static TagCompound Scope(int version) => new(){["FormatVersion"]=version,["StartedAtUnixMilliseconds"]=1234567L,["Entries"]=new List<TagCompound>()};
    /// <summary>按稳定页面与可选世界匹配视图。</summary>
    private static bool Match(TagCompound t,string page,string world) => t.GetString("Tab")==page && (world==null ? !t.ContainsKey("WorldId") : t.GetString("WorldId")==world);
    /// <summary>构造列表视图标签。</summary>
    private static TagCompound View(string page,string column,string direction,float scroll,string world)
    { var t=new TagCompound{["Tab"]=page,["SortColumn"]=column,["SortDirection"]=direction,["ScrollPosition"]=scroll};if(world!=null)t["WorldId"]=world;return t; }
    /// <summary>构造分组状态标签。</summary>
    private static TagCompound Fold(string page,string path,bool expanded,string world)
    { var t=new TagCompound{["Tab"]=page,["GroupPath"]=path,["Expanded"]=expanded};if(world!=null)t["WorldId"]=world;return t; }
    /// <summary>通过真实生产反序列化器读入构造人物。</summary>
    private static object Read(TagCompound root)
    { object[] args={new TagCompound{["CharacterStatistics"]=root},null,null};Check((bool)serializer.GetMethod("TryDeserialize",Flags).Invoke(null,args),"character loads: "+args[2]);return args[1]; }
    /// <summary>保存生产人物模型。</summary>
    private static TagCompound Save(object model) => (TagCompound)serializer.GetMethod("Serialize",Flags).Invoke(null,new[]{model});
    /// <summary>经过真实压缩 TagIO 的内存保存与重读。</summary>
    private static TagCompound Compressed(TagCompound root)
    { using var stream=new MemoryStream();TagIO.ToStream(root,stream,true);stream.Position=0;return TagIO.FromStream(stream,true); }
    /// <summary>失败立即报告检查语义。</summary>
    private static void Check(bool value,string message) {checks++;if(!value)throw new Exception(message);}
}
