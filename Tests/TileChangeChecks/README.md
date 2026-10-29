# 物块采集行为回归

在仓库根目录运行：

```powershell
dotnet run --project Tests/TileChangeChecks/TileChangeChecks.csproj
```

项目直接链接生产环境的 `TileContentSnapshot` 与 `TileChangeScope`，用内存中的模拟世界提供必要的 Terraria 格位、玩家与多格对象接口。断言围绕成功、失败、嵌套、对象完整性、替换语义和生命周期边界，不读写真实存档，不启动 tModLoader，也不依赖 NuGet 测试框架。

当前共 39 项行为检查，包含服务器专用结果出口、嵌套原版入口去重、完整家具同类型拆后重放及世界切换失效；服务器尝试读取角色 ModPlayer 会令回归失败。协议与网络归属另由 `Tests/ConstructionChecks` 的 M4 集成回归检查。

这些检查不验证 Terraria 的真实对象元数据、原版内部 Hook 顺序、ImproveGame 的实际施工或多人网络。生产模组编译、打包和游戏内回归须另外执行并分别报告。测试源码与测试构建产物从模组编译和打包中排除。
