using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Loader;
/// <summary>加载已编译模组与游戏自带序列化库，用构造数据验证升级，不读取存档。</summary>
internal static class Program
{
    /// <summary>参数为 tModLoader 安装目录及本次 Compile 生成的模组 DLL 绝对路径。</summary>
    private static void Main(string[] args)
    {
        if(args.Length!=2 && args.Length!=3) throw new ArgumentException("Expected tModLoader directory, compiled mod DLL path, and optional isolated .tmod path.");
        var paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in Directory.EnumerateFiles(Path.GetFullPath(args[0]),"*.dll",SearchOption.AllDirectories))
            paths.TryAdd(Path.GetFileNameWithoutExtension(path),path);
        AssemblyLoadContext.Default.Resolving+=(context,name)=>paths.TryGetValue(name.Name,out string path)
            ? context.LoadFromAssemblyPath(path):null;
        FormatChecks.Run(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1])));
        if(args.Length==3) PackageChecks.Run(args[2],args[1]);
    }
}
