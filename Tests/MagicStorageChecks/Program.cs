using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using Mono.Cecil;

/// <summary>读取实际魔法存储包的程序集和源码，用于合成入口审计及隔离验证。</summary>
internal static class Program
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>参数为游戏目录、魔法存储包和可选依赖包；提取物只写入仓库 obj 目录。</summary>
    private static void Main(string[] args)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(args[0], "*.dll", SearchOption.AllDirectories))
            paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        AssemblyLoadContext.Default.Resolving += (context, name) => paths.TryGetValue(name.Name, out var path) ? context.LoadFromAssemblyPath(path) : null;
        Assembly runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(args[0], "tModLoader.dll")));
        Type fileType = runtime.GetType("Terraria.ModLoader.Core.TmodFile", true);
        string output = Path.GetFullPath("obj/MagicStorageAudit");
        Directory.CreateDirectory(output);
        foreach (string package in args.Skip(1).Where(p => p.EndsWith(".tmod", StringComparison.OrdinalIgnoreCase)))
        {
            object file = Activator.CreateInstance(fileType, Flags, null, new object[] { Path.GetFullPath(package), null, null }, null);
            using var opened = (IDisposable)fileType.GetMethod("Open", Flags).Invoke(file, null);
            string mod = (string)fileType.GetProperty("Name", Flags).GetValue(file);
            Console.WriteLine($"PACKAGE: {mod} {fileType.GetProperty("Version", Flags).GetValue(file)}");
            MethodInfo bytesMethod = fileType.GetMethod("GetBytes", Flags, null, new[] { typeof(string) }, null);
            byte[] bytes = (byte[])bytesMethod.Invoke(file, new object[] { mod + ".dll" });
            File.WriteAllBytes(Path.Combine(output, mod + ".dll"), bytes);
            if (mod != "MagicStorage") continue;
            using var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
            var type = assembly.MainModule.GetType("MagicStorage.CraftingGUI");
            foreach (var method in type.Methods.Where(m => new[] { "Craft", "Craft_DoStandardCraft", "Craft_WithRecursion", "HandleCraftWithdrawAndDeposit", "AttemptLazyBatchCraft", "AttemptCraft" }.Contains(m.Name)))
            {
                Console.WriteLine(method.FullName);
                File.WriteAllText(Path.Combine(output, method.Name + ".il.txt"), string.Join(Environment.NewLine, method.Body.Instructions));
            }
            foreach (string typeName in new[] { "MagicStorage.Common.Systems.RecurrentRecipes.OrderedRecipeTree", "MagicStorage.Common.Systems.RecurrentRecipes.CraftingSimulation" })
                foreach (var method in assembly.MainModule.GetType(typeName).Methods.Where(m => m.HasBody))
                    File.WriteAllText(Path.Combine(output, typeName.Split('.').Last() + "." + method.Name + ".il.txt"), string.Join(Environment.NewLine, method.Body.Instructions));
            var recursiveStep = type.Methods.Single(m => m.Name == "Craft_DoRecursionCraft");
            File.WriteAllText(Path.Combine(output, "Craft_DoRecursionCraft.il.txt"), string.Join(Environment.NewLine, recursiveStep.Body.Instructions));
            // 本机包启用 includeSource，直接使用包内源码核对对应发行版，避免把仓库最新源码当作当前安装行为。
            foreach (object entry in (IEnumerable)file)
            {
                string name = (string)entry.GetType().GetProperty("Name", Flags)?.GetValue(entry);
                name ??= (string)entry.GetType().GetField("Name", Flags)?.GetValue(entry);
                if (name != null && (name == "NetHelper.cs" || name.EndsWith("CraftingGUI.cs") || name.EndsWith("CraftingGUI.Crafting.cs") || name.EndsWith("CraftingGUI.CraftingHelpers.cs") || name.EndsWith("CraftingGUI.AmountCraftable.cs") || name.EndsWith("CraftingGUI.Caching.cs") || name.EndsWith("MagicStorageConfig.cs") || name.Contains("RecurrentRecipes/") && name.EndsWith(".cs")))
                {
                    byte[] source = (byte[])bytesMethod.Invoke(file, new object[] { name });
                    File.WriteAllBytes(Path.Combine(output, Path.GetFileName(name)), source);
                    Console.WriteLine("SOURCE: " + name);
                }
            }
        }
        foreach (string path in Directory.EnumerateFiles(output, "*.dll")) paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        string compiled = args.Skip(1).SingleOrDefault(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (compiled != null)
        {
            Assembly production = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(compiled));
            Assembly upstream = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(output, "MagicStorage.dll"));
            Type gui = upstream.GetType("MagicStorage.CraftingGUI", true);
            foreach (MethodInfo method in gui.GetMethods(Flags).Where(m => new[] { "Craft", "Craft_DoStandardCraft", "Craft_DoRecursionCraft", "Craft_WithRecursion", "AttemptLazyBatchCraft", "AttemptCraft", "HandleCraftWithdrawAndDeposit" }.Contains(m.Name)))
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
            MethodInfo[] closures = gui.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(Flags)).Where(method => method.Name.StartsWith("<Craft>b__", StringComparison.Ordinal) || method.Name.StartsWith("<Craft_WithRecursion>b__", StringComparison.Ordinal)).ToArray();
            foreach (MethodInfo method in closures) RuntimeHelpers.PrepareMethod(method.MethodHandle);
            Type collector = production.GetType("ComprehensiveStatistics.Common.Systems.MagicStorageCraftingSystem", true);
            var installed = new List<IDisposable>();
            try
            {
                collector.GetMethod("InstallHooks", Flags).Invoke(null, new object[] { upstream, installed, true });
                int normalClosures = closures.Count(method => method.Name.StartsWith("<Craft>b__", StringComparison.Ordinal));
                if (installed.Count != 6 + normalClosures) throw new Exception("Unexpected audited hook count.");
                Console.WriteLine($"MAGIC_STORAGE_ACTUAL_HOOKS_OK: {installed.Count} hooks installed on precompiled entries, including {normalClosures} compiler closures and material acceptance IL boundary");
                int normalCount = installed.Count;
                foreach (string name in new[] { "OrderedRecipeTree", "CraftingSimulation" })
                    foreach (MethodInfo method in upstream.GetType("MagicStorage.Common.Systems.RecurrentRecipes." + name).GetMethods(Flags).Where(m => m.GetMethodBody() != null))
                        RuntimeHelpers.PrepareMethod(method.MethodHandle);
                collector.GetMethod("InstallRecursionHooks", Flags).Invoke(null, new object[] { upstream, installed });
                Console.WriteLine($"MAGIC_STORAGE_RECURSIVE_HOOKS_OK: {installed.Count - normalCount} recursive hooks on precompiled production nodes, accepted iterations and cached simulation binding");
                int previousCount = installed.Count;
                foreach (MethodInfo method in upstream.GetType("MagicStorage.NetHelper").GetMethods(Flags).Where(m => m.Name is "SendCraftRequest" or "ReceiveCraftRequest"))
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                collector.GetMethod("InstallNetworkHooks", Flags).Invoke(null, new object[] { upstream, installed });
                if (installed.Count - previousCount != 4) throw new Exception("Expected four network hooks.");
                Console.WriteLine("MAGIC_STORAGE_NETWORK_HOOKS_OK: actual precompiled request sender, receiver and settlement hooks coexist with ordinary and recursive collection");
            }
            finally { collector.GetMethod("DisposeHooks", Flags).Invoke(null, new object[] { installed }); }
            Scenarios.Run(production);
            ConfirmationChecks.Run();
            var chinese = (Hjson.JsonObject)Hjson.HjsonValue.Parse(File.ReadAllText("Localization/zh-Hans_Mods.ComprehensiveStatistics.hjson"))["UI"]["CraftingCompatibility"];
            var english = (Hjson.JsonObject)Hjson.HjsonValue.Parse(File.ReadAllText("Localization/en-US_Mods.ComprehensiveStatistics.hjson"))["UI"]["CraftingCompatibility"];
            if (!chinese.Keys.OrderBy(key => key).SequenceEqual(english.Keys.OrderBy(key => key))) throw new Exception("Crafting compatibility translations have mismatched keys.");
            Console.WriteLine($"MAGIC_STORAGE_LOCALIZATION_OK: both HJSON files parsed, {chinese.Count} compatibility keys synchronized");
        }
    }
}
