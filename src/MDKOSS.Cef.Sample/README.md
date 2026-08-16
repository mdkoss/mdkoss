# MDKOSS.Cef.Sample

CEF 宿主，用来加载并运行 [`configs/sample.setting.json`](configs/sample.setting.json)。产品版本与仓库说明见根目录 [readme.md](../../readme.md) 与 [src/README.md](../README.md)（当前 **1.2.0**）。

驱动、设备、轴、平台、任务、排单、报警、视觉、启动页都来自这份 JSON；本工程不写机型流程，也不覆盖配置。

可用 `--setting path\to\file.json` 换另一份 setting。

## 运行

```bash
dotnet run --project src/MDKOSS.Cef.Sample/MDKOSS.Cef.Sample.csproj -c Debug
```

或 Visual Studio 将 `MDKOSS.Cef.Sample` 设为启动项目。

默认监控地址见 JSON 的 `monitoringPrefix`（当前为 `http://127.0.0.1:5081/`）。

**启动页**是组态运行页 `index_hmi.html`（`startPage`）。布局在同目录 [`configs/hmi.layout.json`](configs/hmi.layout.json)，由 `MDKOSS.Cef.Extensions` 插件提供页面与 `/api/hmi`。编辑：运行页「编辑组态」→ `/man_hmi.html`，或 Config.Wpf 的 HMI 模块。订单列表等通用页仍在 `/index.html`。

改回旧主界面：把 `startPage` 设为 `index.html`。

**views**：通过 `ProjectReference` 引用 `MDKOSS.Cef`，构建时 Content 落到输出目录 `views/`，不要再 Link 一遍。

## 依赖

- `MDKOSS.Core` / `MDKOSS.Extensions` / `MDKOSS.Cef`
- `MdkPlugins.targets` 复制 Drivers / Extensions 到 `plugins/`（不含 `MDKOSS.Sample.Pnp`）
