# 兼容状态与中英文提示回归

在仓库根目录运行，`TmlPath` 指向本机 tModLoader 安装目录：

```powershell
dotnet run --project Tests/CompatibilityStatusChecks/CompatibilityStatusChecks.csproj -p:TmlPath=D:/Steam/steamapps/common/tModLoader -- .
```

链接生产能力枚举、只读状态模型及提示生成器，使用 tModLoader 自带 Hjson 解析器读取两份真实语言文件。验证全部语言键/占位符对应，以及未安装、未适配版本、单人、部分能力、多人等待、无回应、无共同能力和协商完成状态。测试不访问角色数据，不启动游戏，不替代窗口在实际分辨率下的视觉验收。
