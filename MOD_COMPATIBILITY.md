# 跨模组兼容维护

本文按模组分别维护兼容入口、版本策略和多人确认边界。通用统计与界面规则见 [DESIGN.md](DESIGN.md)。

## ImproveGame（更好的体验）

### 支持范围

- ImproveGame 为可选依赖，最低版本 1.8.2。后续版本默认继续适配，按真实方法签名与能力检测启用，不维护逐版本白名单。
- 本机入口基准为 ImproveGame 1.8.2、SilkyUIFramework 0.3.0；最低版本判断位于 `ImproveGameVersionPolicy`。某组入口缺失只关闭该组，保留其他统计和历史数据。
- 多人双方必须使用相同兼容协议，并协商出共同能力；物块页 `?` 显示九项工具实际启用、等待或未启用状态。原版液体能力不依赖 ImproveGame，也不计入九项魔杖能力。
- 涂漆、生态转换、自动钓鱼和其他自定义机制不在本适配范围。

### 工具与入口

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

#### 关键实现边界

- 建造只处理模板真实目标，不能用包围框填补空洞；构造最终草类型只记一次。直接选中的目标被其他选中目标联动拆除时，可消费破坏前证据；选区外不扩张归属。
- 家具使用 `FacilityRegistry` 归一到完整对象及规范样式；未完整识别/拆放不按组成格兜底。墙分次拆放分别结算，机器人固定出生所有者，连锁首格与扩展矿格去重。
- 审计匹配声明类型、静态性、返回值、参数和迭代器执行体。空间放置枚举为 byte，保留 ref bool；`NetModule.Sender` 是受保护 int 属性，辅助类为 `ImproveGame.Helpers.MyUtils`。
- 采集叶子先于共享 Selector 和接收入口安装。液体组按叶子、MoveNext、工厂、DoSelector 顺序安装，重建预编译框选调用方，避免工厂旧内联副本绕过归属捕获。

### 多人确认与预算

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

### 代码导航与维护

| 职责 | 位置（Common 下） |
| --- | --- |
| 施工快照与结算 | `Statistics/TileContentSnapshot.cs`、`TileChangeScope.cs` |
| 工具及可选依赖安装 | `Systems/ImproveGameCompatibilitySystem.cs`、`ImproveGameConstructionSystem.cs`、`ImproveGameToolSystem.cs` |
| 握手、权威任务与结果 | `Systems/ImproveGameMultiplayerSystem.cs` |
| 客户端施工 / 原版液体证据 | `Systems/ImproveGameClientVerification.cs`、`ImproveGameLiquidVerification.cs` |
| 原版及魔杖液体采集 | `Systems/StatisticsLiquidSystem.cs`、`Statistics/LiquidChangeScope.cs` |
| 协议与状态 | `Networking/ImproveGameProtocol.cs`、`Statistics/ImproveGameCompatibilityStatus.cs` |

变更上游入口、能力、协议或预算时同步修改两端校验和状态文案，保留预编译、异步归属及真实同步顺序检查。运行方式由对应测试说明维护：[施工与多人](Tests/ConstructionChecks/README.md)、[单人采集](Tests/LiquidCollectionChecks/README.md)、[真实入口审计](Tests/LiquidEntryAudit/README.md)。

## MagicStorage（魔法存储）

### 支持范围

- 可选依赖，最低版本 0.7.0.11，后续版本按实际入口、上下文字段和唯一材料成功边界安装。当前真实程序集基准为 MagicStorage 0.7.0.11 / SerousCommonLib 1.0.6.2；包内对应源码已与 IL 核对。普通组不匹配时关闭存储合成；递归组不匹配只回滚递归能力，保留普通合成、原版统计及历史记录。
- 当前启用单人及多人普通、批量和递归合成，递归按设计规范的全链口径结算。多人双方须更新本模组并支持相同独立协议，按实际安装能力取交集；未协商的能力不计数。原版界面合成仍按原规则工作。

### 普通与批量合成

- `Craft(int)` 固定操作及本地玩家，`Craft_DoStandardCraft(CraftingContext)` 限定真实普通合成作用域；四参数 `RecipeLoader.OnCraft` 在回调结束后仅捕获当前上下文结果列表持有的产物。排除 `SimulatingCrafts` 和上下文 `simulation`，同一对象的重复回调更新快照，不重复累计；多个实际新产物对象各自计量。
- `HandleCraftWithdrawAndDeposit` 的材料提取循环成功后会清空临时已取材料列表，然后枚举结果列表；在唯一 `List<Item>.Clear → ldarg.2 → GetEnumerator` 边界标记材料已接受。提取失败的回退路径跳过此处；提交列表还须与该合成上下文压缩后的 `toWithdraw/results` 为同一对象。结果入库及多余产物交付正常完成后才入账，存储满但成功交付的产物仍计数。
- 叶子先安装，再重编译 `AttemptLazyBatchCraft`、`AttemptCraft` 和普通步骤；0.7.0.11 的 `<Craft>b__*` 编译器闭包也重编译，最后安装 `Craft(int)` 根入口，避免上游预编译内联绕过作用域或采集。断线、换世界和异常不保留待提交候选，不引入网络包或修改上游库存行为。

### 递归合成

- 递归组在 `OrderedRecipeTree.GetCraftingInformation` 的节点产物乘以批次后采集主产物和附加产物，早于共享余量计数器被下游消耗。`CraftingSimulation.SimulateCrafts_Inner` 每轮开始清候选，仅在 `CraftResult.CombineWith → simulationResult` 成功合并后保留，失败末轮不计。
- `SimulateCrafts` 正常完成后，以弱键绑定不可变标量计划到原缓存对象。计划保存全链产量减最终余量的链内消耗部分；`Craft_DoRecursionCraft` 取得实际使用的缓存对象时核对世界代次及 AmountCrafted，最后加真实产物后置回调的交付部分。不能由 UsedRecipes 去重集合推产量，不能重跑模拟或从库存净差推测；缺失或不一致的计划不退化为最终余量统计。
- 递归组独立处理节点、迭代、模拟、实际递归步骤及 `<Craft_WithRecursion>b__*` 闭包的预编译调用，再重建递归工厂和根入口。缓存只包含标量并随世界切换失效，不持久化上游运行时 ID 或共享计数器。

### 多人确认与预算

- 多人客户端在 `NetHelper.SendCraftRequest` 前，以同一发送锁发 Begin 标记；随后执行原上游请求，不改变其包内容、库存或掉落。SHA-256 指纹包含存储核心位置、两个列表的顺序、稳定物品键、稳定前缀键及数量，名字长度有界。客户端不上传统计数量或递归计划。
- 服务器在真实 `MessageBuffer.whoAmI` 与 `ReceiveCraftRequest` 的 Sender 一致、握手就绪时消费唯一标记。`HandleCraftWithdrawAndDeposit` 的实际解析列表匹配指纹，且经过材料成功边界、结算正常返回、接收根流程正常完成后，才向该发起者回传一次成功确认；早退、回退、异常和不匹配均不确认成功。`CraftResult` 只是余量返还，不能作为通用成功通知。确认不承诺完整配方防作弊重算。
- 客户端按会话及序号去重；只有本地合成流程正常完成且服务器确认成功才提交。异常发送 Abort 撤销未消费标记；确认超时、队列超限、断线及世界切换时丢弃候选，不跨会话补发。多个请求可以等待确认，服务器每连接只持有一个紧邻实际请求的标记；重叠歧义标记丢弃，下一连续序号可恢复。

| 魔法存储独立协议 / 限额 | 当前值 |
| --- | --- |
| 协议 / 能力 | 1；普通和批量=1，递归=2 |
| 本模组包编号 | 17–22：Hello、Welcome、Ready、Begin、Ack、Abort；不复用 ImproveGame 包 |
| 正文预算 | ushort 长度，最多 128 字节；完整正文解析后再修改状态 |
| 会话身份 | 客户端角色/世界随机挑战 + 服务器每连接随机身份 |
| 请求与去重 | 连续 ulong 操作序号，实际请求消费一次标记，Ack 不携带统计增量 |
| 单请求指纹 | 最多 16384 个输入/结果堆叠；稳定名字各最多 1025 UTF-8 字节 |
| 客户端候选 | 最多 128 个操作，每操作 4096 个物品类型，全部操作共 16384 个标量项 |
| 有效期 | 1800 次游戏更新；超时不补记 |
| 握手重试 | 每 180 次更新一次，最多 10 次 |

### 代码导航与维护

实现入口：`Common/Systems/MagicStorageCraftingSystem.cs`、`MagicStorageRecursion.cs`、`MagicStorageMultiplayer.cs`、`MagicStorageCompatibilityText.cs`；有界模型为 `Common/Networking/MagicStorageConfirmation.cs`。物品页 `?` 显示两项能力及协商状态，按 Shift 查看口径和等待确认数。程序集审计、真实 Hook 安装及隔离场景运行方式见 [MagicStorage 检查](Tests/MagicStorageChecks/README.md)。

## AutoReroll（自动重铸）

- 本机基准为 AutoReroll 2024.8.31；适配该模组提供的前缀列表重铸窗口。可选安装，按实际实例方法及槽位字段签名检测，失败仅关闭本兼容并记录日志，不影响原版统计。
- 对 GadgetBox.GadgetUI.ReforgeMachineUI.ReforgeItem(bool) 的每次真实操作绑定 reforgeSlot.item；StatisticsReforgeSystem 在该作用域内优先使用自定义槽位，捕获 Player.BuyItem(long, int) 返回成功的实际铜币费用。仍由标准 PostReforge 消费既有快照一次，允许上游在重铸时克隆物品；不额外提交第二份次数或支出。
- 手动按钮 OnReforgeButtonClick 和自动连续重铸 Update 共用该叶子；安装叶子后重建两个调用方，避免预编译内联绕过作用域。离开单次操作或异常时清除未消费付款；上游枚举前缀会主动调用 PostReforge，预览入口不建立付费作用域。
- 单人与多人均沿用本地角色的原版重铸归属，不增加网络协议或服务器统计。只统计实际成功的金币重铸，不记录特殊货币、预览、失败扣款和历史漏计；Calamitas 附魔、重铸退款等其他机制不在本次适配范围。
- 实现为 Common/Systems/AutoRerollCompatibilitySystem.cs、StatisticsReforgeSystem.cs 及既有 StatisticsPlayer 快照消费者。真实入口审计、隔离回归与游戏验收见 [自动重铸检查](Tests/AutoRerollChecks/README.md)。
