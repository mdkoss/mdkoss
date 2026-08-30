# MDKOSS.UI.WPF

Prism + WPF 主界面宿主，对应 CEF 的 `index.html` 操作员 HMI。产品版本见根目录 [readme.md](../../readme.md) 与 [src/README.md](../README.md)。

驱动、设备、任务、配方、报警来自 [`configs/sample.setting.json`](configs/sample.setting.json)。可用 `--setting path\to\file.json` 换配置。

## 运行

```bash
dotnet run --project src/MDKOSS.UI.WPF/MDKOSS.UI.WPF.csproj -c Debug
```

或 Visual Studio / VS Code 将 `MDKOSS.UI.WPF` 设为启动项目。

默认监控地址见 JSON 的 `monitoringPrefix`（当前为 `http://127.0.0.1:5083/`，避免与 CEF Sample 的 5081 冲突）。

## 界面

- 顶栏：组件 / 任务 / 变量 / 报警 / 工单 / 用户 / 关于
- 主区：订单列表（Prism `ContentRegion`）
- 底栏：当前订单、状态灯、配方、启动 / 停止 / 复位
- 关于页可进入监控 / 调试 / 配置工具页（第一版为运行时快照表）

## 依赖

- Prism.DryIoc 9
- `MDKOSS.Core` / `MDKOSS.Extensions`
- `MdkPlugins.targets` 复制 Drivers / Extensions 到 `plugins/`（不含 `MDKOSS.Sample.Pnp`）
