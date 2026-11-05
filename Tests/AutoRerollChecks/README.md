# 自动重铸检查

在仓库根目录执行，包路径按实际安装版本调整：

~~~powershell
dotnet build ComprehensiveStatistics.csproj -p:BuildMod=false -p:TargetFramework=net8.0 --no-restore
dotnet run --project Tests/AutoRerollChecks/AutoRerollChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- D:/Steam/steamapps/common/tModLoader D:/Steam/steamapps/workshop/content/1281930/2707400823/2024.6/AutoReroll.tmod bin/Debug/net8.0/ComprehensiveStatistics.dll
dotnet run --project Tests/AutoRerollChecks/Scenarios/Scenarios.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader
~~~

入口检查只读真实 AutoReroll 包，将程序集和相关 IL 提取到忽略的 obj/AutoRerollAudit。审计实际扣款、自定义槽位、完成回调、两个付费调用方及会触发回调的未付费预览，再预编译实际入口、安装并释放三个生产 Hook。成功标记为 AUTO_REROLL_IL_OK 和 AUTO_REROLL_ACTUAL_HOOKS_OK。

隔离场景直接编译生产 AutoRerollCompatibilitySystem 与 StatisticsReforgeSystem，使用真实 MonoMod Hook 和替身玩家/物品，不访问存档。覆盖手动、自动、未付费预览、扣款拒绝、特殊货币、缺少完成回调、异常清理、零费用成功、自定义槽位优先且不重复、普通购买排除、原版重铸回归。快照消费者是替身，只验证这次新增的费用捕获、传递与作用域清理，不代表真实物品、库存、统计存档或界面验收。成功标记为 AUTO_REROLL_SCENARIOS_OK。

## 游戏内验收

1. 构建并重载本模组，日志应显示 AutoReroll ... paid reforge statistics enabled。记录经济页目标物品在生涯和当前世界的次数、费用。
2. 在截图所示界面放入一件物品，手动重铸一次，次数仅 +1，费用增量等于实际支付铜币；考虑哥布林实际折扣，不使用物品基础价格推算。
3. 勾选目标前缀进行自动连续重铸，累计每次成功重铸的实际支付总额与次数；停止后打开统计窗口核对两个范围。
4. 仅放入物品、生成或选择前缀列表、浏览及退出界面均不新增统计；没有足够钱币且扣款失败时也不增长。
5. 切回原版重铸界面，确认每次仍只记录一次；保存退出重进后记录保留。
6. 多人分别以主机和加入者操作，确认只有操作者自身增长；不把本机隔离检查当成真实联机验收。
