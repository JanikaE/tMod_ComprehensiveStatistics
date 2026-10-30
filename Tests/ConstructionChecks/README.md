# 施工与多人检查

在仓库根目录执行，TmlPath 指向实际安装目录：

```powershell
dotnet run --project Tests/ConstructionChecks/ConstructionChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader
dotnet run --project Tests/ConstructionChecks/ConstructionChecks.csproj -c Release -p:TmlPath=D:/Steam/steamapps/common/tModLoader
```

直接链接生产采集器、作用域和多人协议，使用真实 MonoMod Hook、模拟上游工具及独立客户端/服务器格位。覆盖实际拆放、异步归属、候选空洞、掉落同步顺序、四种液体、环境包夹杂、重放、竞争、取消、断线、非法包、预算及可选依赖。

Debug 和 Release 都需通过；可追加参数 `99.0` 检查相同入口在未来版本号下的安装策略，不代表验证了该发行版。格位与网络为隔离夹具，真实主机/专用服务器行为需另行验证。
