using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using Mono.Cecil;

/// <summary>只读审计实际游戏和魔杖程序集；不启动游戏、不执行工具、不访问人物或世界存档。</summary>
internal static class Program
{
    /// <summary>读取游戏方法及模组包中的真实 IL，列出液体采集与多人归属入口。</summary>
    private static void Main(string[] args)
    {
        if (args.Length != 2 && args.Length != 4)
            throw new ArgumentException("Expected tModLoader directory, ImproveGame.tmod path, and optional compiled mod DLL plus extracted dependency directory.");
        string gamePath = Path.GetFullPath(args[0]);
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gamePath, "tModLoader.dll"));
        Console.WriteLine($"GAME: {game.Name.Version}");
        PrintType(game, "Terraria.Player", names: new[] { "Bucket", "Sponge" });
        PrintType(game, "Terraria.MessageBuffer", names: new[] { "GetData" });
        PrintType(game, "Terraria.NetMessage", names: new[] { "sendWater" });
        // TmodFile 自己解释包布局，避免手写压缩/索引解析误读不同发行版。
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(gamePath, "*.dll", SearchOption.AllDirectories))
            paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        AssemblyLoadContext.Default.Resolving += (context, name) => paths.TryGetValue(name.Name, out string path)
            ? context.LoadFromAssemblyPath(path) : null;
        Assembly runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gamePath, "tModLoader.dll"));
        Type fileType = runtime.GetType("Terraria.ModLoader.Core.TmodFile", throwOnError: true);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        object file = Activator.CreateInstance(fileType, flags, binder: null,
            args: new object[] { Path.GetFullPath(args[1]), null, null }, culture: null);
        using var opened = (IDisposable)fileType.GetMethod("Open", flags).Invoke(file, null);
        Console.WriteLine($"PACKAGE: {fileType.GetProperty("Name", flags)?.GetValue(file)} {fileType.GetProperty("Version", flags)?.GetValue(file)}");
        byte[] bytes = (byte[])fileType.GetMethod("GetBytes", flags, binder: null,
            types: new[] { typeof(string) }, modifiers: null).Invoke(file, new object[] { "ImproveGame.dll" });
        using var upstream = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, writable: false));
        foreach (string name in new[] { "ImproveGame.Content.Items.LiquidWand", "ImproveGame.Content.Items.SelectorItem",
            "ImproveGame.Content.Packets.SelectorItemOperation" })
            PrintType(upstream, name, all: true);
        foreach (TypeDefinition type in EnumerateTypes(upstream.MainModule.Types)
            .Where(type => type.BaseType?.FullName == "ImproveGame.Content.Items.LiquidWand"))
            PrintType(upstream, type.FullName, all: true);
        CheckEntries(game, upstream);
        if (args.Length == 4)
        {
            foreach (string path in Directory.EnumerateFiles(Path.GetFullPath(args[3]), "*.dll"))
                paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            AuditHooks(bytes, args[2]);
        }
        Console.WriteLine("LIQUID_ENTRY_AUDIT_READ_COMPLETE");
    }

    /// <summary>在真实安装程序集上安装并释放生产 Hook，验证原版事件与共享 Selector Hook 可共存，不执行游戏逻辑。</summary>
    private static void AuditHooks(byte[] upstreamBytes, string compiledDll)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        Assembly upstream = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(upstreamBytes, false));
        Assembly production = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(compiledDll));
        Type hookType = Assembly.Load("MonoMod.RuntimeDetour").GetType("MonoMod.RuntimeDetour.Hook", true);
        IList hooks = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(hookType));
        Type collectorType = production.GetType("ComprehensiveStatistics.Common.Systems.StatisticsLiquidSystem", true);
        object collector = Activator.CreateInstance(collectorType, true);
        try
        {
            // 实际 tML 先预编译上游、后运行 PostSetupContent；先准备真实调用方，不能只安装冷入口。
            MethodInfo selection = upstream.GetType("ImproveGame.Content.Items.SelectorItem", true)
                .GetMethod("DoSelector", flags);
            RuntimeHelpers.PrepareMethod(selection.MethodHandle);
            collectorType.GetMethod("Load", flags).Invoke(collector, null);
            if (!(bool)collectorType.GetProperty("NativeBoundaryInstalled", flags).GetValue(null))
                throw new Exception("Actual native liquid write boundary was not found.");
            collectorType.GetMethod("InstallWandHooks", flags).Invoke(null, new object[] { upstream, hooks });
            if (hooks.Count != 4) throw new Exception("Expected four actual wand hooks including the pre-JIT selection boundary.");
            Type tools = production.GetType("ComprehensiveStatistics.Common.Systems.ImproveGameToolSystem", true);
            tools.GetMethod("InstallSelectors", flags).Invoke(null, new object[] { upstream, hooks });
            Type multiplayer = production.GetType("ComprehensiveStatistics.Common.Systems.ImproveGameMultiplayerSystem", true);
            Type capability = production.GetType("ComprehensiveStatistics.Common.Networking.ImproveGameCapability", true);
            hooks.Add(multiplayer.GetMethod("CreateReceiverHook", flags).Invoke(null, new object[]
                { upstream, "ImproveGame.Content.Packets.SelectorItemOperation", Enum.ToObject(capability, 262) }));
            if (hooks.Count != 7) throw new Exception("Expected seven coexisting liquid/selector/receiver hooks.");
            Console.WriteLine("LIQUID_ACTUAL_HOOKS_OK: actual ingress IL boundary; pre-JIT selection; 6 native events, 4 wand hooks, 2 shared selector hooks and 1 protocol-3 liquid receiver hook");
        }
        finally
        {
            for (int i = hooks.Count - 1; i >= 0; i--) ((IDisposable)hooks[i]).Dispose();
            collectorType.GetMethod("Unload", flags).Invoke(collector, null);
        }
    }

    /// <summary>验证本阶段依赖的真实入口形状，缺失或改变时明确失败，避免把输出完成误作审计通过。</summary>
    private static void CheckEntries(AssemblyDefinition game, AssemblyDefinition upstream)
    {
        RequireMethod(game, "Terraria.Player", "ItemCheck_UseBuckets", "System.Void", "Terraria.Item");
        RequireMethod(game, "Terraria.NetMessage", "sendWater", "System.Void", "System.Int32", "System.Int32");
        RequireMethod(game, "Terraria.MessageBuffer", "GetData", "System.Void", "System.Int32", "System.Int32", "System.Int32&");
        RequireMethod(upstream, "ImproveGame.Content.Items.LiquidWand", "ModifySelectedTiles", "System.Boolean",
            "Terraria.Player", "System.Int32", "System.Int32");
        RequireMethod(upstream, "ImproveGame.Content.Items.LiquidWand", "PostModifyTiles", "System.Void",
            "Terraria.Player", "System.Int32", "System.Int32", "System.Int32", "System.Int32");
        RequireMethod(upstream, "ImproveGame.Content.Items.SelectorItem", "ModifyTiles", "System.Collections.IEnumerator",
            "Terraria.Player", "Microsoft.Xna.Framework.Rectangle");
        RequireMethod(upstream, "ImproveGame.Content.Packets.SelectorItemOperation", "Receive", "System.Void");
        TypeDefinition advanced = upstream.MainModule.GetType("ImproveGame.Content.Items.LiquidWandAdvanced");
        if (advanced?.BaseType?.FullName != "ImproveGame.Content.Items.LiquidWand"
            || advanced.Methods.Any(method => method.Name == "ModifySelectedTiles"))
            throw new InvalidOperationException("Advanced wand inheritance changed; audit its modification path again.");
        Console.WriteLine("LIQUID_ENTRY_SIGNATURES_OK: 7 entry signatures and advanced-wand inheritance");
    }

    /// <summary>按返回值及完整参数列表确认唯一方法，防止反射名称相同而签名不匹配。</summary>
    private static void RequireMethod(AssemblyDefinition assembly, string typeName, string name,
        string result, params string[] parameters)
    {
        TypeDefinition type = assembly.MainModule.GetType(typeName);
        if (type?.Methods.Count(method => method.Name == name && method.ReturnType.FullName == result
            && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters)) != 1)
            throw new MissingMethodException(typeName, name);
    }

    /// <summary>递归遍历嵌套类型以找到实际执行的迭代器和闭包。</summary>
    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition child in EnumerateTypes(type.NestedTypes)) yield return child;
        }
    }

    /// <summary>输出方法签名与相关 IL，保留原始偏移供审阅，不将发现入口等同于安装 Hook 成功。</summary>
    private static void PrintType(AssemblyDefinition assembly, string name, bool all = false, string[] names = null)
    {
        TypeDefinition type = EnumerateTypes(assembly.MainModule.Types).Single(type => type.FullName == name);
        Console.WriteLine($"TYPE: {type.FullName} : {type.BaseType}");
        if (all) foreach (FieldDefinition field in type.Fields) Console.WriteLine($"FIELD: {field}");
        foreach (MethodDefinition method in type.Methods.Where(method => all || names.Any(name => method.Name.Contains(name))))
        {
            Console.WriteLine($"METHOD: {method}");
            if (!method.HasBody) continue;
            var instructions = method.Body.Instructions;
            for (int index = 0; index < instructions.Count; index++)
            {
                var instruction = instructions[index];
                // GetData 很大，保留液体相关调用前后指令以核对读取顺序和权威边界。
                if (method.Name == "GetData" && !Enumerable.Range(Math.Max(0, index - 45),
                    Math.Min(instructions.Count, index + 46) - Math.Max(0, index - 45))
                    .Any(near => instructions[near].Operand?.ToString()?.Contains("liquid", StringComparison.OrdinalIgnoreCase) == true
                        || instructions[near].Operand?.ToString()?.Contains("sendWater") == true)) continue;
                Console.WriteLine(instruction);
            }
        }
        if (all)
            foreach (TypeDefinition nested in type.NestedTypes) PrintType(assembly, nested.FullName, all: true);
    }
}
