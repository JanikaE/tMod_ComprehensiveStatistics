using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria.ModLoader.IO;

/// <summary>对真实生产液体差量、双范围入口及压缩存档执行隔离检查，不操作玩家存档。</summary>
internal static class LiquidDataChecks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Type snapshotType, changeType, keysType, liquidType, characterType, serializerType;
    private static int checks;

    /// <summary>检查数据边界、类型变化守恒、跨步骤累计、世界隔离及格式 4 持久化。</summary>
    internal static void Run(Assembly assembly)
    {
        snapshotType = Find(assembly, "LiquidSnapshot");
        changeType = Find(assembly, "LiquidChange");
        keysType = Find(assembly, "StatisticKeys");
        liquidType = Find(assembly, "LiquidKind");
        characterType = Find(assembly, "CharacterStatisticsData");
        serializerType = Find(assembly, "StatisticsDataSerializer");
        Check((int)snapshotType.GetField("UnitsPerFullTile", Flags).GetRawConstantValue() == 255, "full cell has 255 units");
        Check(!SnapshotSucceeds(0, -1) && !SnapshotSucceeds(0, 256), "reject out of range volume");
        Check(!SnapshotSucceeds(-1, 1) && !SnapshotSucceeds(4, 1), "reject unknown nonempty liquid");
        foreach (int staleType in new[] { -1, 0, 1, 2, 3, 999 })
        {
            object empty = Snapshot(staleType, 0);
            Check((byte)Get(empty, "Units") == 0 && (int)Get(empty, "Kind") == 0, "normalize empty type");
        }

        string[] names = { "Water", "Lava", "Honey", "Shimmer" };
        for (int kind = 0; kind < names.Length; kind++)
        {
            object placed = Key(kind, true), removed = Key(kind, false);
            Check((string)Get(placed, "StatisticId") == "blocks/liquids-placed", "placement ID");
            Check((string)Get(removed, "StatisticId") == "blocks/liquids-removed", "removal ID");
            Check(Get(placed, "Content").ToString() == "Terraria/Liquid" + names[kind]
                && Get(removed, "Content").Equals(Get(placed, "Content")), "stable semantic liquid key");
            Check((string)Get(placed, "Style") == "" && !placed.Equals(removed), "directions do not share key");
        }
        foreach (int invalid in new[] { -1, 4, int.MaxValue })
        {
            try { Key(invalid, true); throw new Exception("invalid enum accepted"); }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentOutOfRangeException) { checks++; }
        }

        // 覆盖所有类型组合及空格、最小量、部分量、近满格、满格；独立断言逐类型质量守恒。
        int[] volumes = { 0, 1, 80, 100, 128, 254, 255 };
        for (int oldKind = 0; oldKind < 4; oldKind++)
        for (int newKind = 0; newKind < 4; newKind++)
        foreach (int before in volumes)
        foreach (int after in volumes)
        {
            object change = Change(oldKind, before, newKind, after);
            int removed = (int)Get(change, "RemovedUnits"), placed = (int)Get(change, "PlacedUnits");
            Check(removed >= 0 && removed <= 255 && placed >= 0 && placed <= 255, "nonnegative cell bounds");
            Check((bool)Get(change, "IsZero") == (before == after && (before == 0 || oldKind == newKind)), "zero iff same effective state");
            for (int liquid = 0; liquid < 4; liquid++)
            {
                int initial = oldKind == liquid ? before : 0, final = newKind == liquid ? after : 0;
                int outgoing = (int)Get(change, "RemovedKind") == liquid ? removed : 0;
                int incoming = (int)Get(change, "PlacedKind") == liquid ? placed : 0;
                Check(initial - outgoing + incoming == final, "per-liquid volume conservation");
            }
            if (before > 0 && after > 0 && oldKind == newKind)
                Check(placed == 0 || removed == 0, "same type cannot create opposing volume in one step");
            if (before > 0 && after > 0 && oldKind != newKind)
                Check(removed == before && placed == after, "type replacement preserves both complete directions");
        }

        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        DateTimeOffset started = DateTimeOffset.FromUnixTimeMilliseconds(1234567);
        object data = Activator.CreateInstance(characterType, Flags, null, new object[] { started }, null);
        Add(data, a, Change(0, 0, 3, 0), started.AddDays(1));
        Check(Save(data).GetList<TagCompound>("WorldSlices").Count == 0, "empty change does not create world");
        Add(data, a, Change(0, 100, 0, 255), started.AddDays(1));
        Add(data, a, Change(2, 80, 2, 0), started.AddDays(1));
        // 每次一单位连续累计，不能逐次按格舍入；100 -> 255 与额外 100 单位合计正好 255。
        for (int i = 0; i < 100; i++) Add(data, a, Change(0, 0, 0, 1), started.AddDays(2));
        Add(data, a, Change(3, 255, 0, 0), started.AddDays(2));
        Add(data, a, Change(0, 0, 3, 255), started.AddDays(2));
        Add(data, b, Change(0, 0, 1, 7), started.AddDays(3));
        TagCompound saved = Save(data);
        ValidateTotals(saved, a, b, started);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var stream = new MemoryStream();
            TagIO.ToStream(new TagCompound { ["CharacterStatistics"] = saved }, stream, true);
            using var input = new MemoryStream(stream.ToArray());
            object[] read = { TagIO.FromStream(input, true), null, null };
            Check((bool)serializerType.GetMethod("TryDeserialize", Flags).Invoke(null, read), "liquid compressed read: " + read[2]);
            data = read[1]; saved = Save(data);
            ValidateTotals(saved, a, b, started);
        }

        // 将接近 long 上限的真实已存体积再提交增量，验证使用统一饱和累计而不溢出。
        Entry(saved.GetCompound("Career"), "Water", true)["Units"] = long.MaxValue - 1;
        object[] saturated = { new TagCompound { ["CharacterStatistics"] = saved }, null, null };
        Check((bool)serializerType.GetMethod("TryDeserialize", Flags).Invoke(null, saturated), "large liquid total loads");
        Add(saturated[1], a, Change(0, 0, 0, 5), started.AddDays(4));
        Check(Entry(Save(saturated[1]).GetCompound("Career"), "Water", true).GetLong("Units") == long.MaxValue, "saturating volume");
        Check(Entry(World(Save(saturated[1]), a), "Water", true).GetLong("Units") == 260, "scope totals saturate independently");

        CheckMalformedEntries(saved);
        CheckScopeStream(data);
        Console.WriteLine($"LIQUID_DATA_CHECKS_OK: {checks} assertions; volume conservation, stable keys, dual scopes, exact integer accumulation, compressed reload, sparse fields, saturation and malformed isolation");
    }

    /// <summary>逐次核对三份持久化范围、非零指标字段和最初开始时间。</summary>
    private static void ValidateTotals(TagCompound saved, Guid a, Guid b, DateTimeOffset started)
    {
        Check(saved.GetInt("FormatVersion") == 4, "liquid root format");
        TagCompound career = saved.GetCompound("Career"), first = World(saved, a), second = World(saved, b);
        Check(career.GetLong("StartedAtUnixMilliseconds") == started.ToUnixTimeMilliseconds(), "career start preserved");
        Check(first.GetLong("StartedAtUnixMilliseconds") == started.AddDays(1).ToUnixTimeMilliseconds()
            && second.GetLong("StartedAtUnixMilliseconds") == started.AddDays(3).ToUnixTimeMilliseconds(), "world starts isolated");
        foreach (TagCompound scope in new[] { career, first })
        {
            Check(scope.GetInt("FormatVersion") == 4, "liquid scope format");
            Check(Entry(scope, "Water", true).GetLong("Units") == 255, "raw addition stays exact");
            Check(Entry(scope, "Honey", false).GetLong("Units") == 80, "honey removal independent");
            Check(Entry(scope, "Shimmer", true).GetLong("Units") == 255 && Entry(scope, "Shimmer", false).GetLong("Units") == 255,
                "remove and replace not cancelled by net difference");
            foreach (var entry in scope.GetList<TagCompound>("Entries"))
                Check(!entry.ContainsKey("Events") && !entry.ContainsKey("Amount") && entry.GetLong("Units") > 0, "volume only uses sparse Units");
        }
        Check(Entry(career, "Lava", true).GetLong("Units") == 7 && Entry(second, "Lava", true).GetLong("Units") == 7, "second world routed to career");
        Check(first.GetList<TagCompound>("Entries").Count == 4 && second.GetList<TagCompound>("Entries").Count == 1, "world liquid records isolated");
    }

    /// <summary>验证损坏的液体体积条目只丢弃自身，合法历史不受影响。</summary>
    private static void CheckMalformedEntries(TagCompound saved)
    {
        var entries = saved.GetCompound("Career").GetList<TagCompound>("Entries");
        int expected = entries.Count;
        entries.Add(new TagCompound { ["StatisticId"] = "blocks/liquids-placed", ["ContentMod"] = "Terraria", ["ContentName"] = "LiquidHoney", ["Units"] = -1L });
        entries.Add(new TagCompound { ["StatisticId"] = "blocks/liquids-placed", ["ContentMod"] = "Terraria", ["Units"] = 5L });
        entries.Add(new TagCompound { ["StatisticId"] = "blocks/liquids-placed", ["ContentMod"] = "Terraria", ["ContentName"] = "LiquidHoney", ["Units"] = 0L });
        object[] read = { new TagCompound { ["CharacterStatistics"] = saved }, null, null };
        Check((bool)serializerType.GetMethod("TryDeserialize", Flags).Invoke(null, read), "malformed entry isolation");
        Check(Save(read[1]).GetCompound("Career").GetList<TagCompound>("Entries").Count == expected, "no negative, incomplete or zero liquid persisted");
    }

    /// <summary>格式 4 共用范围流保存原始长整数；此为序列化检查，不启用液体多人协议。</summary>
    private static void CheckScopeStream(object data)
    {
        using var stream = new MemoryStream();
        object career = Get(data, "Career");
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            serializerType.GetMethod("WriteScope", Flags).Invoke(null, new[] { writer, career });
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        object[] read = { reader, null, null };
        Check((bool)serializerType.GetMethod("TryReadScope", Flags).Invoke(null, read), "current scope stream reads");
        object metrics = read[1].GetType().GetMethod("GetValue", Flags).Invoke(read[1], new[] { Key(0, true) });
        Check((long)Get(metrics, "Units") == 255L && (long)Get(metrics, "Events") == 0L, "stream keeps raw volume");
    }

    /// <summary>按统计方向及稳定液体键查找压缩存档中的唯一条目。</summary>
    private static TagCompound Entry(TagCompound scope, string liquid, bool placed) => scope.GetList<TagCompound>("Entries")
        .Single(tag => tag.GetString("StatisticId") == (placed ? "blocks/liquids-placed" : "blocks/liquids-removed")
            && tag.GetString("ContentMod") == "Terraria" && tag.GetString("ContentName") == "Liquid" + liquid);

    /// <summary>按世界 UUID 查找切片，避免假定字典保存顺序。</summary>
    private static TagCompound World(TagCompound saved, Guid world) => saved.GetList<TagCompound>("WorldSlices")
        .Single(tag => tag.GetString("WorldId") == world.ToString("N")).GetCompound("Data");

    /// <summary>通过生产快照工厂检查非法输入是否被拒绝。</summary>
    private static bool SnapshotSucceeds(int kind, int units)
    {
        object[] args = { kind, units, null };
        return (bool)snapshotType.GetMethod("TryCreate", Flags).Invoke(null, args);
    }

    /// <summary>建立合法测试快照，拒绝把工厂失败的默认值当作空格使用。</summary>
    private static object Snapshot(int kind, int units)
    {
        object[] args = { kind, units, null };
        if (!(bool)snapshotType.GetMethod("TryCreate", Flags).Invoke(null, args)) throw new Exception("invalid fixture snapshot");
        return args[2];
    }

    /// <summary>使用真实差量实现计算一个即时步骤。</summary>
    private static object Change(int oldKind, int before, int newKind, int after) => changeType.GetMethod("Between", Flags)
        .Invoke(null, new[] { Snapshot(oldKind, before), Snapshot(newKind, after) });

    /// <summary>使用稳定枚举取得真实统计键。</summary>
    private static object Key(int kind, bool placed) => keysType.GetMethod(placed ? "GetPlacedLiquidKey" : "GetRemovedLiquidKey", Flags)
        .Invoke(null, new[] { Enum.ToObject(liquidType, kind) });

    /// <summary>调用角色生产双范围液体入口，不另写一份模拟累计逻辑。</summary>
    private static void Add(object data, Guid world, object change, DateTimeOffset now) => characterType.GetMethod("AddLiquidChange", Flags)
        .Invoke(data, new[] { (object)world, change, now });

    /// <summary>调用实际生产序列化器。</summary>
    private static TagCompound Save(object data) => (TagCompound)serializerType.GetMethod("Serialize", Flags).Invoke(null, new[] { data });

    /// <summary>读取非公开生产标量，避免测试工程复制内部数据类型。</summary>
    private static object Get(object value, string property) => value.GetType().GetProperty(property, Flags).GetValue(value);

    /// <summary>解析同一生产命名空间的内部类型。</summary>
    private static Type Find(Assembly assembly, string name) => assembly.GetType("ComprehensiveStatistics.Common.Statistics." + name, true);

    /// <summary>累计检查数量并给出失败约束。</summary>
    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        checks++;
    }
}
