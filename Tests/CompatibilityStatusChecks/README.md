# 双语与兼容状态检查

```powershell
dotnet run --project Tests/CompatibilityStatusChecks/CompatibilityStatusChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- .
```

加载生产能力、状态及提示生成器，读取真实中英文 Hjson，检查语言键/占位符、版本门槛、缺失依赖、部分能力、等待和协商状态，以及默认精简提示和 Shift 完整提示。不启动 UI，也不访问角色统计。
