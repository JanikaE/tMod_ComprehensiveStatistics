# 数据版本升级回归

先运行模组 Compile 目标，再将以下两个路径替换为本机 tModLoader 安装目录及生成的模组 DLL：

```powershell
dotnet run --project Tests/DataFormatChecks/DataFormatChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- D:/Steam/steamapps/common/tModLoader "C:/Users/Administrator/Documents/My Games/Terraria/tModLoader/ModSources/ComprehensiveStatistics/obj/Debug/net8.0/ComprehensiveStatistics.dll"
```

程序在独立进程中加载真实 TagCompound 库和生产序列化器，分别构造版本 1、2 标签，检查生涯、世界切片、世界数据、开始时间、折叠状态和旧网络范围在升级后仍保留，并验证输出版本 3 及拒绝未知未来版本。

`PersistenceChecks.cs` 另对版本 1、2、3 执行真实压缩 TagIO 内存保存/重载，检查两个世界切片、人物生涯、新增活动双范围写入、原始开始时间和独立折叠状态。缺失模组内容键、家具样式和布线 Events/Units 必须原样保留；兼容握手、任务、候选等运行时状态不得进入角色存档。新增 99 项持久化断言，不从旧放置合计补算旧拆除。

独立进程只注册测试需要的原版布尔标签序列化器，不启动游戏、不读取或保存用户角色及世界；生产模组不引用此项目。
