using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Terraria.ModLoader.IO;

/// <summary>验证合成数量的独立持久化、第四列筛选合计和精确排序，不读取用户存档。</summary>
internal static class CraftingChecks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static int checks;

    /// <summary>对真实生产模型执行格式 4 和第四列端到端检查。</summary>
    internal static void Run(Assembly assembly)
    {
        Type serializer = assembly.GetType("ComprehensiveStatistics.Common.Statistics.StatisticsDataSerializer");
        var scope = new TagCompound { ["FormatVersion"] = 4, ["StartedAtUnixMilliseconds"] = 1234567L, ["Entries"] = new List<TagCompound> {
            Entry("items/pickups", 7), Entry("items/crafts", long.MaxValue), Entry("items/discards", 2) } };
        Guid world = Guid.NewGuid();
        var root = new TagCompound { ["FormatVersion"] = 4, ["Career"] = scope,
            ["WorldSlices"] = new List<TagCompound> { new() { ["WorldId"] = world.ToString("N"), ["Data"] = scope } },
            ["ListViewStates"] = new List<TagCompound> { new() { ["Tab"] = "Items", ["SortColumn"] = "CraftedUnits",
                ["SortDirection"] = "Descending", ["ScrollPosition"] = 34f } } };
        object[] input = { new TagCompound { ["CharacterStatistics"] = root }, null, null };
        Check((bool)serializer.GetMethod("TryDeserialize", Flags).Invoke(null, input), "craft data load");
        var saved = (TagCompound)serializer.GetMethod("Serialize", Flags).Invoke(null, new[] { input[1] });
        foreach (var data in new[] { saved.GetCompound("Career"), saved.GetList<TagCompound>("WorldSlices")[0].GetCompound("Data") })
        {
            var entries = data.GetList<TagCompound>("Entries");
            Check(entries.Single(t => t.GetString("StatisticId") == "items/crafts").GetLong("Units") == long.MaxValue, "craft units retain exact long in both scopes");
            Check(entries.Single(t => t.GetString("StatisticId") == "items/pickups").GetLong("Units") == 7, "pickup remains separate");
            Check(entries.Single(t => t.GetString("StatisticId") == "items/discards").GetLong("Units") == 2, "discard remains separate");
        }
        Check(saved.GetInt("FormatVersion") == 4 && saved.GetList<TagCompound>("ListViewStates")[0].GetString("SortColumn") == "CraftedUnits", "format and sort state preserved");
        Type window = assembly.GetType("ComprehensiveStatistics.Common.UI.StatisticsWindowState");
        object career = Get(input[1], "Career");
        object projectedGroup = ((IEnumerable)window.GetMethod("CreateItemGroups", Flags).Invoke(null, new[] { career })).Cast<object>().Single();
        object projectedItem = ((IEnumerable)Get(projectedGroup, "Items")).Cast<object>().Single();
        Check((long)Get(projectedItem, "CraftedUnits") == long.MaxValue && (long)Get(projectedItem, "Units") == 7
            && (long)Get(projectedItem, "Amount") == 2 && (long)Get(projectedItem, "Events") == 0, "real item page projection keeps all four columns independent");

        Type itemType = assembly.GetType("ComprehensiveStatistics.Common.UI.Elements.StatisticsListItem");
        Type groupType = assembly.GetType("ComprehensiveStatistics.Common.UI.Elements.StatisticsListGroup");
        object first = Make(itemType, new Dictionary<string, object> { ["name"] = "OnlyCrafted", ["craftedUnits"] = long.MaxValue, ["source"] = EnumValue(assembly, "StatisticsContentSource", "Vanilla") });
        object second = Make(itemType, new Dictionary<string, object> { ["name"] = "Other", ["units"] = 19L, ["craftedUnits"] = 3L, ["source"] = EnumValue(assembly, "StatisticsContentSource", "Modded") });
        Array items = Array.CreateInstance(itemType, 2); items.SetValue(first, 0); items.SetValue(second, 1);
        object group = Make(groupType, new Dictionary<string, object> { ["name"] = "Root", ["items"] = items });
        Check((long)Get(group, "TotalCraftedUnits") == long.MaxValue, "craft sum saturates");
        Check((long)Get(group, "TotalUnits") == 19, "fourth metric does not change pickup sum");
        Type listType = assembly.GetType("ComprehensiveStatistics.Common.UI.Elements.VirtualizedStatisticsList");
        object list = RuntimeHelpers.GetUninitializedObject(listType);
        listType.GetField("searchQuery", Flags).SetValue(list, "OnlyCrafted");
        object filtered = listType.GetMethod("BuildFilteredGroup", Flags).Invoke(list, new[] { group, (object)false });
        Check((long)Get(filtered, "TotalCraftedUnits") == long.MaxValue && (long)Get(filtered, "TotalUnits") == 0, "craft-only item survives filtering with independent totals");
        listType.GetField("sourceFilter", Flags).SetValue(list, EnumValue(assembly, "StatisticsContentSourceFilter", "Modded"));
        Check(listType.GetMethod("BuildFilteredGroup", Flags).Invoke(list, new[] { group, (object)false }) == null, "source and search filters combine");
        Type sibling = listType.GetNestedType("SiblingEntry", Flags);
        object left = sibling.GetMethod("ForItem", Flags).Invoke(null, new[] { first });
        object right = sibling.GetMethod("ForItem", Flags).Invoke(null, new[] { second });
        listType.GetField("sortColumn", Flags).SetValue(list, EnumValue(assembly, "StatisticsListSortColumn", "CraftedUnits"));
        foreach (string direction in new[] { "Descending", "Ascending" })
        {
            listType.GetField("sortDirection", Flags).SetValue(list, EnumValue(assembly, "StatisticsListSortDirection", direction));
            int comparison = (int)listType.GetMethod("CompareSiblingEntries", Flags).Invoke(list, new[] { left, right });
            Check(direction == "Descending" ? comparison < 0 : comparison > 0, "craft numeric sorting " + direction);
        }
        Check(typeof(Terraria.ModLoader.RecipeLoader).GetMethod("OnCraft", new[] { typeof(Terraria.Item), typeof(Terraria.Recipe), typeof(Terraria.Item) }) != null, "installed native callback signature");
        VerifyCollection(assembly);
        Console.WriteLine($"CRAFTING_CHECKS_OK: {checks} persistence, independent totals, craft-only row, filters, saturation, sorting and native signature checks");
    }

    /// <summary>验证真实采集叶子在回调结束后取新产物标量，异常和无作用域时不采集。</summary>
    private static void VerifyCollection(Assembly assembly)
    {
        Type system = assembly.GetType("ComprehensiveStatistics.Common.Systems.StatisticsCraftingSystem");
        FieldInfo pending = system.GetField("pendingCrafts", Flags);
        var candidates = new List<(int Type, int Units)>();
        var output = new Terraria.Item { type = 8, stack = 3 };
        var destination = new Terraria.Item { type = 8, stack = 900 };
        Action<Terraria.Item, Terraria.Recipe, Terraria.Item> callback = (item, recipe, target) => item.stack = 6;
        pending.SetValue(null, candidates);
        try
        {
            system.GetMethod("HandleCraft", Flags).Invoke(null, new object[] { callback, output, null, destination });
            Check(candidates.Count == 1 && candidates[0] == (8, 6), "capture post-callback output, excluding destination stack");
            output.stack = 0;
            Check(candidates[0].Units == 6, "later stack merge cannot mutate candidate");
            Action<Terraria.Item, Terraria.Recipe, Terraria.Item> failure = (item, recipe, target) => throw new InvalidOperationException("fixture");
            try { system.GetMethod("HandleCraft", Flags).Invoke(null, new object[] { failure, output, null, destination }); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
            Check(candidates.Count == 1, "callback failure does not append candidate");
            pending.SetValue(null, null);
            system.GetMethod("HandleCraft", Flags).Invoke(null, new object[] { callback, output, null, destination });
            Check(candidates.Count == 1, "no native scope means no candidate");
        }
        finally { pending.SetValue(null, null); }
    }

    /// <summary>构造带缺失模组稳定键的数量条目。</summary>
    private static TagCompound Entry(string id, long units) => new() { ["StatisticId"] = id, ["ContentMod"] = "MissingProvider", ["ContentName"] = "CraftedThing", ["Units"] = units };

    /// <summary>按参数名构造生产模型，其他可选参数沿用生产默认值。</summary>
    private static object Make(Type type, Dictionary<string, object> values)
    {
        var constructor = type.GetConstructors(Flags).Single();
        return constructor.Invoke(constructor.GetParameters().Select(p => values.TryGetValue(p.Name, out object value) ? value : p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray());
    }

    /// <summary>解析生产枚举值。</summary>
    private static object EnumValue(Assembly assembly, string type, string value) => Enum.Parse(assembly.GetType("ComprehensiveStatistics.Common.UI.Elements." + type), value);

    /// <summary>读取生产模型属性。</summary>
    private static object Get(object model, string name) => model.GetType().GetProperty(name, Flags).GetValue(model);

    /// <summary>检查统计不变量。</summary>
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
}
