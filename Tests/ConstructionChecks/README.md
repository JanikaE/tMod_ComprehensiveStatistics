# M2/M3 工具与 M4 多人确认集成回归

运行时将 `TmlPath` 替换为本机 tModLoader 安装目录：

```powershell
dotnet run --project Tests/ConstructionChecks/ConstructionChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader
```

项目链接生产 `ImproveGameConstructionSystem`、`ImproveGameToolSystem`、快照和结算源码，使用游戏自带的真实 MonoMod RuntimeDetour 安装 Hook；上游工具、玩家和格位是隔离的模拟实现，不加载实际 ImproveGame，也不读写用户存档。

同时链接生产 `ImproveGameMultiplayerSystem` 和 `ImproveGameProtocol`。`M4Checks.cs` 使用内存网络投递真实协议字节，模拟两名玩家的握手、服务器接收、延后协程和定向回传；服务器读取 `LocalPlayer` 或 `GetModPlayer` 会直接令回归失败。模拟上游 `Sender` 与本机安装版一致，为受保护属性。测试覆盖会话/批次去重、协议版本、能力交集、无真实发送者入口、异常、断线、超时、有界队列与输出预算，以及截断/超长/非法结果的原子拒绝。

`M4ClientChecks.cs` 与 `ClientNetworkStubs.cs` 保存独立的客户端/服务器格位，不共享 Tile 引用，按实际顺序投递生产窗口协议及模拟原版入站。新增 24 个场景验证四类客户端工具、同类型中间拆除、服务器拒绝、家具完整性、组件位、候选空洞、递归清理、冲突、取消/异常、世界失效、断线与重放。原版采掘包必须先于兼容空缺同步到达，服务器掉落检查用于防止同步顺序改变施工结果。两端运行的是隔离世界，实际 Terraria/TileEntity/专用服务器与主机联机仍需游戏验收。

测试运行真实生成的嵌套 IEnumerator，验证工厂/MoveNext 任务绑定、原函数及 out 参数转发、清理后重建、最终草类型、直接家具、组件、建造替换、多人数值抑制、取消、异常、会话失效和可选依赖降级。仅固定测试入口使用 NoInlining；建造工具的小型 TryPlace/TryPlaceWall 包装使用 AggressiveInlining，迭代器工厂保留默认内联行为，以覆盖 Hook 安装顺序导致漏计的游戏内反馈。

同时运行优化构建，不能只依赖 Debug 结果：

```powershell
dotnet run --project Tests/ConstructionChecks/ConstructionChecks.csproj -c Release -p:TmlPath=D:/Steam/steamapps/common/tModLoader
```

真实安装包的签名/Hook 安装验证、游戏内施工、多人协议和性能仍需分别检查，不能以此回归冒充游戏验收。测试不进入模组编译和打包。

清理回归还覆盖：建筑模板目标被联动提前拆除、模板包围框中的非目标空洞、范围外连带破坏、构造清理的闭区间边界、4×4 分帧批量拆除和失败拆除。成功替换分别断言旧内容拆除与新内容放置；拆旧后放新失败仅增加实际拆除。

`M3Checks.cs` 与 `M3FakeUpstream.cs` 覆盖空间魔杖真实 byte 枚举/ref 参数转发、同类型实际拆后重放、仅坡形变化、法爆/星爆固定及框选拆除、分帧框选墙、机器人生成时所有者与世界身份、失效任务、取消、异常后保留实际结果并释放上下文、内置连锁首格与扩展矿格去重、无效矿工、远程玩家、多人数值抑制及卸载。Debug 和 Release 均须通过。
