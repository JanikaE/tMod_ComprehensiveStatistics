# 实际程序集入口审计

在仓库根目录执行，替换为实际安装与 ImproveGame 包路径：

```powershell
dotnet run --project Tests/LiquidEntryAudit/LiquidEntryAudit.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- D:/Steam/steamapps/common/tModLoader D:/Steam/steamapps/workshop/content/1281930/2797518634/2026.3/ImproveGame.tmod
```

读取本机程序集元数据和 IL，检查原版桶/海绵入口、消息 48、魔杖普通/高级继承、Selector 工厂、迭代器和接收签名。完整输出可重定向到被忽略的 `audit-output.txt`。

追加本次模组 DLL 和包依赖提取目录（如 `bin/Debug/net8.0/ComprehensiveStatistics.dll obj/M1Audit`）可执行实际 Hook 安装/释放检查：先预编译 DoSelector，再验证采集与共享入口共存，以及服务器液体写入后的 IL 注入。依赖目录需包含该安装包的 NetSimplified 等依赖，不能用未知来源文件替代。

`LIQUID_ENTRY_SIGNATURES_OK` 表示签名通过，`LIQUID_ACTUAL_HOOKS_OK` 表示可选安装检查通过。程序不执行工具、不启动世界、不读写人物存档；完整输出和测试文件不进入模组包。
