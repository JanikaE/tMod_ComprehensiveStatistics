# 存档与列表模型检查

先编译模组，在仓库根目录执行：

```powershell
dotnet run --project Tests/DataFormatChecks/DataFormatChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- D:/Steam/steamapps/common/tModLoader bin/Debug/net8.0/ComprehensiveStatistics.dll
```

加载真实生产程序集与 TagIO，以内存构造标签检查格式升级、未来格式拒绝、压缩重载、双范围和世界隔离、缺失内容、样式、稀疏字段和饱和保护。子页检查覆盖迁移不覆盖新状态、选择容错、筛选合计、精确排序、体积格式和按钮换行。钓鱼检查覆盖三个子页的固定顺序、鱼饵视图整体迁移、液体折叠迁移、选择及新状态保留、鱼竿结果与未分类历史的合计、来源筛选和鱼竿名称搜索。

可在命令末尾追加隔离 `.tmod` 路径，核对包内 DLL 和两个图标与编译结果/源文件逐字节一致。生产 UI 的图标依赖通过回调隔离，不验证真实绘制、字体、缩放或输入法；不读取用户存档。
