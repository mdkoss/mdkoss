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
├── MDKOSS.Extensions.Camera/
├── MDKOSS.Extensions.PyScript/
├── MDKOSS.Extensions.ModServer/
├── MDKOSS.Cef/
├── MDKOSS.Sample/
└── MDKOSS.Config/
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
| `MDKOSS.Extensions.Camera` | `extcamera` | `CameraExtensionBootstrap` |
| `MDKOSS.Extensions.ModServer` | `devmodserver` | `ModServerExtensionBootstrap` |
| `MDKOSS.Extensions.PyScript` | `devpyscript` | `PyScriptExtensionBootstrap` |

## 延伸阅读

- [extensions.md](./extensions.md)
- [configuration.md](./configuration.md)
- [core-subsystems.md](./core-subsystems.md)
