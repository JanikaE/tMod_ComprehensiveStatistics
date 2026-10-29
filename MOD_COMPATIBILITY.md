# Comprehensive Statistics（综合统计）跨模组兼容维护说明

> 当前状态：ImproveGame 兼容专项 M1–M5 已完成；用户于 2026-10-29 确认游戏内验收全部完成。
> 当前版本：兼容协议 `2`；统计存档格式 `3`，兼容读取格式 `1/2`。
> 通用规范：[DESIGN.md](DESIGN.md)。本文维护具体模组的最终规则、实现入口和支持边界；后续新增兼容模组时另设章节。

## 1. ImproveGame：支持范围与版本

兼容“更好的体验”（[ImproveGame](https://github.com/487666123/ImproveGame)）中可明确归属玩家的主动施工，将结果并入既有物块、背景墙、家具、电线和制动器统计。液体、涂漆、生态转换、自动钓鱼及其他类别不在本专项范围内。

- 目标环境为 tModLoader `2026.8 Stable`（Terraria `1.4.4.9`）；本机验证运行时为 `2026.8.3.0`。
- 适配范围为 **ImproveGame `>= 1.8.2`**，无版本上限；默认后续更新延续当前兼容逻辑，不要求每次更新重新加入版本白名单。统一版本判断位于 `Common/Networking/ImproveGameVersionPolicy.cs`。
- 运行时继续逐能力核对真实方法签名，并按实际安装及多人协商结果启用。若上游发生破坏性变更，相关能力安全降级，届时再重新适配；不能将仅版本号更新判为不支持。
- 本机实际程序集审计及加载验证使用 ImproveGame `1.8.2`，其依赖 SilkyUIFramework 为 `0.3.0`。源码依据为上游 `1.4.4` 分支提交 [`5f25aad630df6bc0a700771deb333bb47b4c05ef`](https://github.com/487666123/ImproveGame/tree/5f25aad630df6bc0a700771deb333bb47b4c05ef)（源码版本 `1.8.3`）；不据此扩大为所有发行版的游戏验证。
- ImproveGame 为可选依赖；未加载、版本不支持或某组 Hook 安装失败时，关闭相应能力，保留普通统计及历史记录。多人还须双方协议一致且协商出共同能力。

### 工具覆盖与定位

下表的“执行端”指多人模式中的实际世界修改端；单人均在本地确认真实结果。

| 工具 / 能力标识 | 主要入口 | 执行端与关键边界 |
| --- | --- | --- |
| 构造魔杖 `Construct` | `ConstructWand` → `GenerateCore.GenerateFromTag/Generate` 及各施工迭代器 | 客户端；覆盖显式清理、墙、单格物块、完整家具、四色线和制动器。每次实际 `MoveNext` 恢复同一任务归属，停止后仅保留完成部分。 |
| 建造魔杖 `Building` | `CreateWand.TryPlaceBuilding`、`TryPlace/TryPlaceWall` | 客户端；按实际步骤统计预设房屋的清理、替换与施工，清理目标由模板 `TileInfos.HasWall` 等真实条件确定，排除模板空洞。 |
| 空间魔杖 `Space` | `SpaceWandOperation.Receive` → `SpaceWand.OperateTile` | 服务器；按明确目标集合统计放置/替换，单独改变坡形为零。 |
| 法爆 / 星爆框选 `BurstSelector` | `SelectorItemOperation.Receive` → `ModifyTiles/ModifySelectedTiles` | 服务器；覆盖实际拆除的物块、墙和完整容器，协程持续绑定发起者。 |
| 法爆 / 星爆固定模式 `FixedBurst` | `MagickWand.FixedModeAction`（含继承行为） | 客户端；仅确认固定模式直接选中的实际拆除目标。 |
| 科技魔杖框选 `WallSelector` | `WallPlaceSelectorMode.ModifySelectedTiles` | 服务器；拆旧墙、放新墙可能分次执行，各自成功后计数。 |
| 科技魔杖机器人 `Robot` | `WallRobot.AI`；出生关联见 `StatisticsGlobalProjectile.OnSpawn` | 客户端；出生时固定所有者和世界会话，换手持后仍沿用；未观察到出生或身份失效时不猜测归属。 |
| 内置连锁挖矿 `Vein` | `VeinMiningPacket.Receive` → `VeinMinerSystem.DoVeinMiningAt/BreakAllOres` | 服务器；明确 `MinerIndex` 与实际矿格清单，首格沿用普通采掘并去重。仅适配 ImproveGame 自身启用的连锁功能。 |

## 2. 最终统计口径

1. **按真实成功变化计数。** 普通物块、墙按格位，家具 / 多格物体按完整对象，四色电线与制动器按实际组件位变化。不按选区面积、模板内容、工具调用次数、材料消耗或掉落量估算；无限材料下的成功施工同样计数。
2. **成功替换同时计旧内容拆除和新内容放置。** 仅拆成功则只计拆除；原为空格则只计成功放置；失败且内容未变则为零。同类型、同规范样式且未实际拆放为零；确实先拆后重建，即使内容相同也分别计两侧。显式清理与随后施工独立结算，不能用任务最终净差抵消。
3. **按实际内容及规范样式归类。** 构造先放泥土再还原草时，只计最终草类型一次；颜色、涂层、坡形、半砖、朝向、开门状态及激活状态的单独调整不生成拆放记录。
4. **只统计直接目标。** 明确选中的目标在同次操作中被其他目标联动提前拆除，仍可凭破坏前证据计入；选区外、模板空洞和环境连带变化不自动归属。选中的家具组成格可扩展为其完整对象范围。
5. **家具必须完整识别与确认。** 复用 `FacilityRegistry` 的类型 / 样式归一化，按原点、类型和样式去重；多个组成格只计一件。身份未知、形成或拆除不完整时跳过，不能按组成格兜底计数。
6. **部分成功保留，归属不猜测。** 取消、异常或材料耗尽只丢弃未完成部分。任务与实体固定关联玩家、世界会话和实际执行步骤；不根据当前手持物品或任意 `Main.LocalPlayer` 推定所有者。离开世界、断线或会话变化后，旧任务不得写入新会话。
7. **使用现有累计入口与筛选。** 来源筛选按被拆放内容所属模组判断，魔杖放原版内容仍属于原版。施工格数不增加工具使用次数，也不新增按工具重复保存的统计项。

## 3. 实现导航与维护约束

以下路径均相对仓库根目录。

| 职责 | 实现位置 |
| --- | --- |
| 内容快照、完整对象识别 | `Common/Statistics/TileContentSnapshot.cs` |
| 嵌套作用域、任务 / 世界身份、直接目标、去重与结算 | `Common/Statistics/TileChangeScope.cs`、`TileChangeResult.cs`、`TileChangeCandidate.cs` |
| 原版共用采集入口 | `Common/Systems/StatisticsTilePlacementSystem.cs`、`StatisticsTileMiningSystem.cs` |
| 可选入口审计、构造 / 建造及其他工具适配 | `Common/Systems/ImproveGameCompatibilitySystem.cs`、`ImproveGameConstructionSystem.cs`、`ImproveGameToolSystem.cs` |
| 握手、服务器任务、结果回传；客户端施工验证 | `Common/Systems/ImproveGameMultiplayerSystem.cs`、`ImproveGameClientVerification.cs`（同一类的分部实现） |
| 能力位、协议校验、包分派 | `Common/Networking/ImproveGameCapability.cs`、`ImproveGameProtocol.cs`、`StatisticsNetcode.cs` |
| 本地角色累计与存档 | `Common/Players/StatisticsPlayer.cs` 的 `RecordConfirmedConstruction`；`Common/Statistics/StatisticsData.cs`、`StatisticsDataSerializer.cs` |
| 状态、双语说明与悬停绘制 | `Common/Statistics/ImproveGameCompatibilityStatus.cs`、`Common/UI/ImproveGameCompatibilityText.cs`、`Elements/CompatibilityInfoButton.cs`、`StatisticsWindowState.cs` |

必须保留的实现约束：

- 快照保存不可变标量，不保留可变 `Tile` 引用；同步步骤结束后结算，不能跨帧对整片建筑做净差推算。最外层语义作用域消费内层采集结果，防止普通采掘、工具入口及兜底各记一次。
- 分帧迭代器在每次 `MoveNext` 恢复上下文并在退出时清理；嵌套迭代器共享任务身份。弱关联中的旧会话身份不能被复用为新任务所有者。
- **Hook 按被调用的叶子方法先、调用入口后安装**，避免 JIT 内联绕过采集。工具采集安装完成后再安装多人接收入口，不依赖 ModSystem 加载顺序。
- 审计必须匹配声明类型、静态性、返回值、参数及迭代器真实执行体。当前公共辅助类为 `ImproveGame.Helpers.MyUtils`；空间放置枚举的底层类型为 `byte`，适配须保留 `ref bool` 语义；`NetModule.Sender` 为受保护的 `int` 属性。
- 探测日志仅说明入口元数据；实际启用状态由成功安装的能力组及本次网络协商共同决定，各组失败独立回退。

## 4. 多人确认流程与预算

### 两类确认路径

- **服务器执行工具：** 在已审计的网络接收入口，将真实 `MessageBuffer.whoAmI` 与上游 `Sender` 校验一致，服务器分配任务身份并传递至后续协程。服务器观察实际变化，聚合后定向回传发起者；其他客户端不入账，服务器不保存角色个人生涯。
- **客户端执行工具：** 首次直接修改前发 `ClientBegin`，随后发送原版施工同步及必要的已完成步骤边界，再发精确的 `ClientTargets` 分片和 `ClientCommit`。候选只有坐标、层、意图与前后状态指纹，不含客户端申报的统计增量。服务器在真实入站原版消息 `17/20/34/79` 的处理边界缓存本连接实际接受的变化，并观察递归拆除；只确认与直接候选、变化链及当前世界状态一致的结果。竞争修改会使相应证据失效。

显式清理必须在重建前形成真实同步边界，保留同类型重建的两侧证据。底层 Kill 回调仅登记待同步空缺，待原版 `PickTile/TryKillTile` 拆除包已发出后再同步仍为空的目标，避免抢先清空服务器格位而影响掉落；不能在重建后伪造历史空格。同步按相邻行段合并并发送完整新家具，不能用包围框填补模板空洞。

握手采用 `Hello → Welcome → Ready`，绑定客户端入世界挑战值与服务器连接身份，按双方实际能力取交集。确认结果带会话、服务器任务号和连接内连续单调批次号；整包内容解析、白名单与稳定键解析通过后才推进游标并入账。重复、乱序、断线、跨世界、无证据及非法结果均拒绝。共享网络流的 `Length` 不是当前模组包长度，协议显式携带 `ushort` 正文长度并在有界子流内解析。

| 预算 / 版本 | 当前值 |
| --- | --- |
| 协议 / 包类型 | 协议 `2`；`8–11` 为握手及确认结果，`12–14` 为客户端施工窗口；既有 `1–7` 不复用 |
| 确认队列 | 每连接最多 `4096` 个待回传聚合项；每项增量至多 `1,000,000` |
| 确认发送 | 每包最多 `24` 项，正文最多 `32 KiB`；每连接每帧最多 `8` 包 |
| 内容名称 | 稳定键的模组名、内容名各最多 `512` UTF-8 字节 |
| 有效期 | 任务 / 队列 / 窗口上限 `1800` 次游戏更新（约 30 秒）；长任务按进度续期 |
| 客户端施工窗口 | 每连接同时一个；最多 `8192` 个实际观察格位、`24576` 个格位层候选 / 证据 |
| 候选分片 | 每片最多 `256` 项，每项 `40` 字节 |
| 完整新家具同步 | 宽、高均不超过 `32` 格；普通相邻行段最多 `32` 格 |

预算约束属于多人兼容确认。窗口超限或新家具不能完整同步时，丢弃该窗口的未确认结果；超时、缺失能力或协议不匹配同样不估算补记，并提供受限频次诊断。断线前未确认动作不跨会话补发。本功能用于个人统计，不提供防作弊或排行榜保证。

## 5. 存档与玩家提示

- 存档格式为 `3`，读取格式 `1/2/3`，拒绝未知未来格式。沿用稳定内容键、规范样式及生涯 / 当前世界切片；四色线与制动器继续使用既有放置 `Events`、拆除 `Units` 字段。
- 保留历史累计、开始时间、世界隔离及各范围折叠状态；历史漏计没有事件证据，不补算、不重算。缺失模组内容的稳定键保留，重新加载后可恢复显示。
- 兼容能力、网络会话、任务、候选和确认队列不持久化；重进世界重新握手。
- 物块页标题的 `?` 显示中英文版本 / 会话状态及八项工具能力（启用、等待或未启用），并说明替换双向计数、历史不补算和多人预算。状态来自实际能力，不向列表插入统计项。
- 状态仅在窗口运行态且悬停时查询；不在 `Load/OnInitialize` 访问角色统计。提示在 `StatisticsWindowState.Draw` 完成子元素绘制后调用 `UICommon.TooltipMouseText`，文字子元素忽略鼠标命中；不要改回 Update 阶段设置临时悬停文本。

## 6. 最终验收与后续维护

**验收已完成，无本轮遗留待验收项。** 用户于 2026-10-29 确认全部游戏内验收完成，涵盖单人 / 多人计数、替换与清理重建、归属与取消、保存重进 / 世界切换、中英文问号提示及范围边界。上文预算与版本限制仍是现行支持边界，不因验收完成而取消；本记录不提供未记录的性能数值或额外版本保证。

现有自动检查与加载证据如下；这些是后续修改的回归入口，自动检查与用户游戏验收分别记录：

| 检查入口 | 已通过的覆盖 |
| --- | --- |
| `Tests/TileChangeChecks` | 39 项共用采集行为检查 |
| `Tests/ConstructionChecks`（Debug / Release） | 真实 Hook 的隔离集成；24 个独立双端施工场景、2435 个协议 / 队列断言，含拆除包顺序与掉落、重复确认、竞争修改及旧会话 |
| `Tests/CompatibilityStatusChecks` | 347 个版本门槛 / 状态 / 双语提示断言，235 个语言键对应 |
| `Tests/DataFormatChecks` | 格式 `1/2 → 3` 保留与未来格式拒绝；新增 99 项真实压缩 TagIO 持久化断言 |
| 实际程序集与加载 | 本机 40 个生产方法 Hook 安装 / 释放；有、无 ImproveGame 的专用服务器加载 / 保存 / 退出 |
| 构建 | Compile、隔离 `.tmod` 打包通过；测试目录排除于正式构建和模组包 |

回归命令在仓库根目录执行，`TmlPath` 替换为实际安装目录。格式检查还需已编译的本模组 DLL：

```powershell
dotnet run --project Tests/TileChangeChecks/TileChangeChecks.csproj
dotnet run --project Tests/ConstructionChecks/ConstructionChecks.csproj -c Release -p:TmlPath=D:/Steam/steamapps/common/tModLoader
dotnet run --no-build --project Tests/ConstructionChecks/ConstructionChecks.csproj -c Release -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- 99.0
dotnet run --project Tests/CompatibilityStatusChecks/CompatibilityStatusChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- .
dotnet run --project Tests/DataFormatChecks/DataFormatChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- D:/Steam/steamapps/common/tModLoader obj/Debug/net8.0/ComprehensiveStatistics.dll
```

构造集成检查可传入模拟上游版本；`99.0` 用于验证相同入口在未来版本号下仍可安装全部能力，不代表实际验证了该发行版。

后续维护按以下顺序核对：

1. ImproveGame 后续更新默认沿用适配，不维护逐版本白名单；实际出现破坏性变更时，再审计程序集、签名、执行端、同步布局及 Hook 安装顺序并修复。tModLoader 更新涉及底层接口变化时同样复核。
2. 新增 / 修改工具：更新覆盖表与能力位，明确直接目标、对象完整性和异步归属；复用共用结算，补充相应双端回归。
3. 修改协议 / 预算：同步更新协议版本、双方校验、限额、降级和双语提示；保留拆除包顺序及有界解析约束。
4. 修改统计语义 / 存档 / UI：同步 [DESIGN.md](DESIGN.md)、本文和中英文资源；保留旧记录并验证保存重载。UI 改动须实际悬停检查。
5. 变更完成：分别记录自动检查、编译 / 打包与游戏验收结论；多人修改需复验主机和专用服务器的交错施工、掉落及边界场景。更新本文的最终状态，不追加无关排查流水。
