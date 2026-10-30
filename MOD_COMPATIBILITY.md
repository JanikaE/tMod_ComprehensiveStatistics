# 跨模组兼容维护

本文维护 ImproveGame 的工具入口、版本策略和多人确认边界。通用统计与界面规则见 [DESIGN.md](DESIGN.md)。

## 1. 支持范围

- ImproveGame 为可选依赖，最低版本 1.8.2。后续版本默认继续适配，按真实方法签名与能力检测启用，不维护逐版本白名单。
- 本机入口基准为 ImproveGame 1.8.2、SilkyUIFramework 0.3.0；最低版本判断位于 `ImproveGameVersionPolicy`。某组入口缺失只关闭该组，保留其他统计和历史数据。
- 多人双方必须使用相同兼容协议，并协商出共同能力；物块页 `?` 显示九项工具实际启用、等待或未启用状态。原版液体能力不依赖 ImproveGame，也不计入九项魔杖能力。
- 涂漆、生态转换、自动钓鱼和其他自定义机制不在本适配范围。

## 2. 工具与入口

| 工具 / 能力 | 多人执行端 | 核心入口 |
| --- | --- | --- |
| 构造 `Construct` | 客户端 | `ConstructWand` → `GenerateCore.GenerateFromTag/Generate` 及施工迭代器 |
| 建造 `Building` | 客户端 | `CreateWand.TryPlaceBuilding`、`TryPlace/TryPlaceWall` |
| 空间 `Space` | 服务器 | `SpaceWandOperation.Receive` → `SpaceWand.OperateTile` |
| 法爆/星爆框选 `BurstSelector` | 服务器 | `SelectorItemOperation.Receive` → `ModifyTiles/ModifySelectedTiles` |
| 法爆/星爆固定范围 `FixedBurst` | 客户端 | `MagickWand.FixedModeAction` |
| 科技墙框选 `WallSelector` | 服务器 | `WallPlaceSelectorMode.ModifySelectedTiles` |
| 墙机器人 `Robot` | 客户端 | `StatisticsGlobalProjectile.OnSpawn` → `WallRobot.AI` |
| 内置连锁挖矿 `Vein` | 服务器 | `VeinMiningPacket.Receive` → `DoVeinMiningAt/BreakAllOres` |
| 普通/高级液体魔杖 `Liquid` | 服务器 | 共享 Selector 任务 → `LiquidWand.ModifySelectedTiles`；高级形态继承同一叶子 |

### 关键实现边界

- 建造只处理模板真实目标，不能用包围框填补空洞；构造最终草类型只记一次。直接选中的目标被其他选中目标联动拆除时，可消费破坏前证据；选区外不扩张归属。
- 家具使用 `FacilityRegistry` 归一到完整对象及规范样式；未完整识别/拆放不按组成格兜底。墙分次拆放分别结算，机器人固定出生所有者，连锁首格与扩展矿格去重。
- 审计匹配声明类型、静态性、返回值、参数和迭代器执行体。空间放置枚举为 byte，保留 ref bool；`NetModule.Sender` 是受保护 int 属性，辅助类为 `ImproveGame.Helpers.MyUtils`。
- 采集叶子先于共享 Selector 和接收入口安装。液体组按叶子、MoveNext、工厂、DoSelector 顺序安装，重建预编译框选调用方，避免工厂旧内联副本绕过归属捕获。

## 3. 多人确认与预算

- 服务器执行工具在真实 `MessageBuffer.whoAmI` 与上游 Sender 一致时创建权威任务；结果聚合后定向回传给发起者。
- 客户端执行工具按 `ClientBegin → 原版同步/步骤边界 → ClientTargets → ClientCommit` 提交直接候选，不申报统计增量；服务器观察消息 17/20/34/79 实际接受的变化和递归拆除证据。
- 拆除同步必须先于重建，保留同类型重建两侧证据；原版采掘包先于兼容空缺同步，避免抢先清空服务器格位而改变掉落。新家具完整同步，普通格位按相邻行段合并。
- 原版桶/海绵按 `NativeLiquidBegin → 消息48 → NativeLiquidCommit` 声明直接包序；服务器在写完数量/类型、反应和帧修复之前的 IL 边界取证。所有实际 SendData(48) 与控制标记共用发送锁，环境包只占序号；方向、类型、范围和基线不匹配时不记。魔杖使用服务器任务的逐格直接体积。
- 握手为 Hello/Welcome/Ready，绑定入世界挑战与连接身份。正文使用 ushort 长度限定；随机会话和连续批次去重，整包解析、白名单及稳定键校验通过后才推进游标并提交。

| 协议 / 限额 | 当前值 |
| --- | --- |
| 协议和能力 | 3；能力字段 ushort，原八个位保留，Liquid=256、NativeLiquid=512 |
| 包类型 | 8–11 握手/结果，12–14 客户端施工，15–16 原版液体控制；1–7 不复用 |
| 每连接确认队列 | 4096 个聚合项；单项增量最多 1,000,000 原始单位，液体超出时分片 |
| 确认发送 | 每包 24 项、正文 32 KiB，每帧 8 包 |
| 稳定键名称 | 模组名、内容名各最多 512 UTF-8 字节 |
| 任务/窗口有效期 | 1800 次游戏更新；有效任务按实际进度续期 |
| 客户端施工窗口 | 同时一个，8192 个观察格位、24576 个格位层候选/证据 |
| 候选分片 | 256 项，每项 40 字节 |
| 新家具及相邻行段 | 家具最大 32×32 格，行段最多 32 格 |
| 原版液体窗口 | 每连接一个，中心 3×3、最多 9 个直接目标、32 个原版包 |

超限、过期、协议不匹配、竞争证据失效或断线时不推算未确认部分；不跨会话补发，也不提供防作弊保证。

## 4. 代码导航与维护

| 职责 | 位置（Common 下） |
| --- | --- |
| 施工快照与结算 | `Statistics/TileContentSnapshot.cs`、`TileChangeScope.cs` |
| 工具及可选依赖安装 | `Systems/ImproveGameCompatibilitySystem.cs`、`ImproveGameConstructionSystem.cs`、`ImproveGameToolSystem.cs` |
| 握手、权威任务与结果 | `Systems/ImproveGameMultiplayerSystem.cs` |
| 客户端施工 / 原版液体证据 | `Systems/ImproveGameClientVerification.cs`、`ImproveGameLiquidVerification.cs` |
| 原版及魔杖液体采集 | `Systems/StatisticsLiquidSystem.cs`、`Statistics/LiquidChangeScope.cs` |
| 协议与状态 | `Networking/ImproveGameProtocol.cs`、`Statistics/ImproveGameCompatibilityStatus.cs` |

变更上游入口、能力、协议或预算时同步修改两端校验和状态文案，保留预编译、异步归属及真实同步顺序检查。运行方式由对应测试说明维护：[施工与多人](Tests/ConstructionChecks/README.md)、[单人采集](Tests/LiquidCollectionChecks/README.md)、[真实入口审计](Tests/LiquidEntryAudit/README.md)。
