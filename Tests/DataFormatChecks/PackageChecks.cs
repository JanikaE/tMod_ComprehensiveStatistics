using System;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>核对隔离打包确实包含本次 DLL 与原始图标，不将打包器图像警告等同于资源丢失。</summary>
internal static class PackageChecks
{
    /// <summary>用游戏原生读取器打开模组包，逐字节比较编译结果及图标源文件。</summary>
    internal static void Run(string package, string compiledDll)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type type = typeof(Terraria.ModLoader.Mod).Assembly.GetType("Terraria.ModLoader.Core.TmodFile", true);
        object file = Activator.CreateInstance(type, flags, null,
            new object[] { Path.GetFullPath(package), null, null }, null);
        using var opened = (IDisposable)type.GetMethod("Open", flags).Invoke(file, null);
        MethodInfo getBytes = type.GetMethod("GetBytes", flags, null, new[] { typeof(string) }, null);
        foreach (var entry in new[] { ("ComprehensiveStatistics.dll", Path.GetFullPath(compiledDll)),
            ("icon.png", Path.GetFullPath("icon.png")), ("icon_small.png", Path.GetFullPath("icon_small.png")) })
        {
            byte[] bytes = (byte[])getBytes.Invoke(file, new object[] { entry.Item1 });
            if (!bytes.SequenceEqual(File.ReadAllBytes(entry.Item2)))
                throw new Exception("Package differs from current build/source: " + entry.Item1);
        }
        Console.WriteLine("PACKAGE_CONTENT_CHECKS_OK: current compiled DLL and both original icons retained byte-for-byte");
    }
}
