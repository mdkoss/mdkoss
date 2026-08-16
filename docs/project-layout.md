# 项目结构与模块职责

## src/ 目录

```text
src/
├── MDKOSS.Core/                 # IDriver + DriverFactory（无内置板卡实现）
├── MDKOSS.Extensions/           # IMdkExtension 接入层
├── MDKOSS.Drivers.Sim/          # sim
├── MDKOSS.Drivers.Gts/          # gts
├── MDKOSS.Drivers.Dmc/          # LTDMC 绑定
├── MDKOSS.Extensions.Serial/
├── MDKOSS.Extensions.Tcp/
├── MDKOSS.Extensions.Mysql/
├── MDKOSS.Extensions.Camera/
├── MDKOSS.Extensions.PyScript/
├── MDKOSS.Extensions.ModServer/
├── MDKOSS.Cef/
├── MDKOSS.Cef.Extensions/       # 主界面监控组态（控件扩展包 + 布局 API）
├── MDKOSS.Cef.Sample/           # 通用 CEF 宿主
├── MDKOSS.Sample/               # SampleExt 扩展示例宿主
├── MDKOSS.Sample.DieBonder/     # 半导体贴片机 Demo 宿主
├── MDKOSS.Sample.Dispenser/
├── MDKOSS.Sample.Pnp/           # 拾取放置 Demo 宿主
└── MDKOSS.Config.Wpf/
```

依赖：Drivers.* / Extensions.* → Extensions → Core。

宿主仅引用 Core + Extensions，构建时由 MdkPlugins.targets 将插件复制到 plugins/，运行时 DiscoverAndRegister() 扫描加载。

## 驱动插件

| 项目 | type | Bootstrap |
|------|------|-----------|
| `MDKOSS.Drivers.Sim` | `sim` | `SimDriverBootstrap` |
| `MDKOSS.Drivers.Gts` | `gts` | `GtsDriverBootstrap` |
| `MDKOSS.Drivers.Dmc` | （待 DrvDmc） | `DmcDriverBootstrap` |

## 设备扩展

| 项目 | type | Bootstrap |
|------|------|-----------|
| `MDKOSS.Extensions.Serial` | `serialdev` | `SerialExtensionBootstrap` |
| `MDKOSS.Extensions.Tcp` | `tcpdev` | `TcpExtensionBootstrap` |
| `MDKOSS.Extensions.Mysql` | `mysqldev` | `MysqlExtensionBootstrap` |
| `MDKOSS.Extensions.Camera` | `extcamera` | `CameraExtensionBootstrap` |
| `MDKOSS.Extensions.ModServer` | `devmodserver` | `ModServerExtensionBootstrap` |
| `MDKOSS.Extensions.PyScript` | `devpyscript` | `PyScriptExtensionBootstrap` |

## HMI 组态

| 项目 | 页面 / API | Bootstrap |
|------|------------|-----------|
| `MDKOSS.Cef.Extensions` | `index_hmi.html` / `man_hmi.html` / `/api/hmi` | `CefHmiExtensionBootstrap` |

## Android

| 目录 | 说明 |
|------|------|
| `android/MdkossIssues` | Issue 提交/管理，JDBC 直连 `mdkossdb`。见 [issues.md](./issues.md) |

## 延伸阅读

- [extensions.md](./extensions.md)
- [configuration.md](./configuration.md)
- [core-subsystems.md](./core-subsystems.md)
- [issues.md](./issues.md)
