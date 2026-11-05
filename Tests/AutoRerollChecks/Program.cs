using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;

/// <summary>只读核对自动重铸真实程序集，并在预编译入口安装生产 Hook。</summary>
internal static class Program
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>参数为游戏目录、AutoReroll 包及本模组编译结果；不执行 UI 或读取存档。</summary>
    private static void Main(string[] args)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(args[0], "*.dll", SearchOption.AllDirectories))
            paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
            paths.TryGetValue(name.Name, out string path) ? context.LoadFromAssemblyPath(path) : null;
        Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(args[0], "tModLoader.dll")));
        Type fileType = game.GetType("Terraria.ModLoader.Core.TmodFile", true);
        object file = Activator.CreateInstance(fileType, Flags, null, new object[] { Path.GetFullPath(args[1]), null, null }, null);
        using var opened = (IDisposable)fileType.GetMethod("Open", Flags).Invoke(file, null);
        Console.WriteLine($"PACKAGE: {fileType.GetProperty("Name", Flags).GetValue(file)} {fileType.GetProperty("Version", Flags).GetValue(file)}");
        byte[] bytes = (byte[])fileType.GetMethod("GetBytes", Flags, null, new[] { typeof(string) }, null)
            .Invoke(file, new object[] { "AutoReroll.dll" });
        string output = Path.GetFullPath("obj/AutoRerollAudit");
        Directory.CreateDirectory(output);
        string dll = Path.Combine(output, "AutoReroll.dll");
        File.WriteAllBytes(dll, bytes);
        using var code = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
        TypeDefinition ui = code.MainModule.GetType("GadgetBox.GadgetUI.ReforgeMachineUI");
        MethodDefinition root = ui.Methods.Single(m => m.Name == "ReforgeItem");
        Check(Calls(root, "Terraria.Player", "BuyItem") == 1, "Unique actual payment");
        Check(Calls(root, "GadgetBox.GadgetMethods", "PrefixItem") == 1, "Unique actual prefix operation");
        Check(root.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "reforgeSlot"), "Uses own slot");
        MethodDefinition prefix = code.MainModule.GetType("GadgetBox.GadgetMethods").Methods.Single(m => m.Name == "PrefixItem");
        Check(Calls(prefix, "Terraria.ModLoader.ItemLoader", "PostReforge") == 1, "Completion callback exists");
        MethodDefinition[] callers = ui.Methods.Where(m => Calls(m, ui.FullName, "ReforgeItem") > 0).ToArray();
        Check(callers.Select(m => m.Name).OrderBy(x => x).SequenceEqual(new[] { "OnReforgeButtonClick", "Update" }), "All paid callers covered");
        Check(ui.Methods.Any(m => m.Name.StartsWith("GetPrefixes") && Calls(m, "Terraria.ModLoader.ItemLoader", "PostReforge") > 0),
            "Unpaid preview has completion callbacks and must remain outside scope");
        foreach (MethodDefinition method in callers.Append(root).Append(prefix))
            File.WriteAllText(Path.Combine(output, method.Name + ".il.txt"), string.Join(Environment.NewLine, method.Body.Instructions));
        Console.WriteLine("AUTO_REROLL_IL_OK: actual payment, own slot, completion, manual/automatic callers and unpaid preview audited");

        Assembly upstream = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        Type runtimeUi = upstream.GetType(ui.FullName, true);
        foreach (MethodDefinition method in callers.Append(root))
            RuntimeHelpers.PrepareMethod(runtimeUi.GetMethod(method.Name, Flags).MethodHandle);
        Assembly production = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
        Type collector = production.GetType("ComprehensiveStatistics.Common.Systems.AutoRerollCompatibilitySystem", true);
        var installed = new List<IDisposable>();
        try
        {
            collector.GetMethod("InstallHooks", Flags).Invoke(null, new object[] { upstream, installed });
            Check(installed.Count == 3, "Three production hooks");
            Console.WriteLine("AUTO_REROLL_ACTUAL_HOOKS_OK: three hooks installed on actual precompiled entries");
        }
        finally
        {
            for (int index = installed.Count - 1; index >= 0; index--) installed[index].Dispose();
        }
    }

    /// <summary>按声明类型及方法名统计真实调用数量。</summary>
    private static int Calls(MethodDefinition method, string type, string name) =>
        method.HasBody ? method.Body.Instructions.Count(i => i.Operand is MethodReference r && r.DeclaringType.FullName == type && r.Name == name) : 0;

    /// <summary>任一入口不匹配即使检查失败。</summary>
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
