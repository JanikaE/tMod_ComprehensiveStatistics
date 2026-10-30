# 单人采集检查

```powershell
dotnet run --project Tests/LiquidCollectionChecks/LiquidCollectionChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader
dotnet build Tests/LiquidCollectionChecks/LiquidCollectionChecks.csproj -c Release -p:TmlPath=D:/Steam/steamapps/common/tModLoader
$env:DOTNET_TieredCompilation = '0'
dotnet Tests/LiquidCollectionChecks/bin/Release/net8.0/LiquidCollectionChecks.dll
Remove-Item Env:DOTNET_TieredCompilation
```

直接编译生产原版桶/海绵与魔杖采集器，以真实 Hook 和隔离格位验证体积、自然更新排除、嵌套步骤、任务身份、异常及卸载。Release 关闭分层编译以覆盖上游先预编译、短工厂内联的安装顺序，不能只运行 Debug。

网络使用隔离桩；多人由 [ConstructionChecks](../ConstructionChecks/README.md) 验证，真实签名由 [入口审计](../LiquidEntryAudit/README.md) 验证。此程序不启动游戏或读写真实存档。
