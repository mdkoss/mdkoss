# MDKOSS.Cef.Sample

CEF 宿主，用来加载并运行 [`configs/sample.setting.json`](configs/sample.setting.json)。产品版本与仓库说明见根目录 [readme.md](../../readme.md) 与 [src/README.md](../README.md)（当前 **1.1.0**）。

驱动、设备、轴、平台、任务、排单、报警、视觉、启动页都来自这份 JSON；本工程不写机型流程，也不覆盖配置。

可用 `--setting path\to\file.json` 换另一份 setting。

## 运行

```bash
dotnet run --project src/MDKOSS.Cef.Sample/MDKOSS.Cef.Sample.csproj -c Debug
```

或 Visual Studio 将 `MDKOSS.Cef.Sample` 设为启动项目。

默认监控地址见 JSON 的 `monitoringPrefix`（当前为 `http://127.0.0.1:5081/`）。启动页见 `startPage`（当前为 `index.html`）。

**views**：通过 `ProjectReference` 引用 `MDKOSS.Cef`，构建时 Content 落到输出目录 `views/`，不要再 Link 一遍。

## 依赖

- `MDKOSS.Core` / `MDKOSS.Extensions` / `MDKOSS.Cef`
- `MdkPlugins.targets` 复制 Drivers / Extensions 到 `plugins/`（不含 `MDKOSS.Pnp`）
