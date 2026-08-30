# MDKOSS.UI.WPF.Sample — WPF 启动与扩展示例

对照 CEF 侧的 `MDKOSS.Cef.Sample` + `MDKOSS.Sample`：本工程是 **Prism WPF 启动宿主**，并演示如何给各扩展模块加原生页。

界面壳、监控 / 调试 / 配置内核页在 [`MDKOSS.UI.WPF`](../MDKOSS.UI.WPF/README.md)。本工程只做：

1. 启动 `MdkWpfHost`
2. 注册 `SampleExt`（自定义设备 / MotionTask / API）
3. 用 `IWpfUiExtension` 挂额外 WPF 页

## 1. 覆盖的模块

| 模块 | 配置 | WPF 页 |
|------|------|--------|
| SampleExt 自定义设备 | `sample-beacon` / `samplemotion` | `monitor_sampleext` · `debug_sampleext` |
| 轴 / 平台 / IO | `axis-*` · `head-demo` · `gpio1` | UI.WPF 内置 `monitor_*` / `debug_*` |
| 串口 | `serial1` | `debug_serial` |
| TCP | `tcp1` | **本工程** `debug_tcp` |
| MySQL | `mysql1` | `debug_mysql` |
| 相机 / 视觉 | `cam1` · `cam-ext-1` · `vision-1` | `debug_camera` · `debug_vision` |
| Python | `py-1` | **本工程** `debug_pyscript` |
| Modbus Server/Client | `mod-1` · `modc-1` | **本工程** `debug_modbus` |

```text
src/MDKOSS.UI.WPF.Sample/
├── Program.cs                 # Register SampleExt + IWpfUiExtension → MdkWpfHost.Run
├── Modules/SampleWpfUiExtension.cs
├── Views/ · ViewModels/       # 仅扩展示例页
├── SampleExt/                 # 链接 MDKOSS.Sample/SampleExt（不含 HTML）
└── configs/sample.setting.json
```

`Program.Main` 在创建 Runtime **之前** `Register(new SampleExtExtension())`，并 `WpfUiExtensionHost.Register(new SampleWpfUiExtension())`。

二次开发加页：实现 `IWpfUiExtension.ToolPage<View, VM>(pageId, group, label)`，不要改 UI.WPF 的 `App.RegisterTypes`。

## 2. 运行

```bash
dotnet run --project src/MDKOSS.UI.WPF.Sample/MDKOSS.UI.WPF.Sample.csproj -c Debug
```

VS Code：`.NET Core Launch (UI.WPF.Sample)`。

监控 HTTP：`http://127.0.0.1:5089/`（避免与 UI.WPF 5083、CEF Sample 5081 冲突）。

手测：主界面 → 关于进监控总览 → 切到「扩展示例」看信标 KPI → 调试页脉冲 / 运动 → TCP / Python / Modbus 页能列出对应设备。
