# src/ — MDKOSS 源码

当前产品版本 **1.2.0**（见 `Directory.Build.props` / `MdkProduct.Version`）。更完整的上手说明见仓库根目录 [readme.md](../readme.md)；架构细项见 [docs/](../docs/README.md)。

## 分层

```text
宿主（Sample / Cef.Sample / UI.WPF / Sample.DieBonder / Sample.Dispenser / Sample.Pnp / Sample.Modbus / Sample.Tools / Config.Wpf / Tools.Calib）
  → Core + Extensions
      ↑
Drivers.* / Extensions.*  （插件 DLL，构建写入 plugins/，运行时 DiscoverAndRegister）
```

- **Core 永不引用扩展或板卡实现。**
- 通信/外设做成 `MDKOSS.Extensions.*`，经 `IMdkExtension` 注册。
- 板卡做成 `MDKOSS.Drivers.*`，实现 `IDriver`，由 Bootstrap 注册 `type`。

## 项目一览

| 项目 | 职责 |
|------|------|
| `MDKOSS.Core` | 运行时内核、设备基类、任务调度、变量中心、监控 HTTP/API、SQLite |
| `MDKOSS.Extensions` | 扩展接入层（注册表 / Host） |
| `MDKOSS.Drivers.Sim` / `Gts` / `Dmc` / `S7` / `Boards` | 仿真 / 固高 / 雷赛 / S7 / 市面常用卡 type |
| `MDKOSS.Extensions.Serial` / `Tcp` / `Mysql` / `Camera` / `PyScript` / `ModServer` | 可选设备扩展（ModServer 含 `modbus` IDriver） |
| `MDKOSS.Cef` | CefSharp 界面库 + `views/` HMI |
| `MDKOSS.Cef.Extensions` | 主界面监控组态（控件扩展包 + `/api/hmi`） |
| `MDKOSS.Cef.Sample` | 通用 CEF 宿主（`startPage`=`index_hmi.html`） |
| `MDKOSS.UI.WPF` | Prism + WPF 主界面宿主（操作员 HMI） |
| `MDKOSS.Sample` | SampleExt 扩展示例宿主 |
| `MDKOSS.Sample.DieBonder` | 半导体贴片机 Demo |
| `MDKOSS.Sample.Dispenser` | 三轴点胶机 Demo |
| `MDKOSS.Sample.Pnp` | 拾取放置（PNP）Demo |
| `MDKOSS.Sample.Modbus` | Modbus IDriver 联调（默认 200 Holding） |
| `MDKOSS.Sample.Tools` | 设备组件调试（轴 / IO / 串口 / TCP / 相机 / 视觉 / MySQL 等） |
| `MDKOSS.Config.Wpf` | 离线配置编辑器 |
| `MDKOSS.Tools.Calib` | WPF 标定工具（Flow / MotionTask；参数与结果入 SQLite） |
| `MDKOSS.Iec61131` | Flow / 变量 / GPIO → IEC 61131-3（SCL + PLCopen XML） |
| `MDKOSS.Sample.Iec61131` | 工位节拍示例 + 导出结果 |

### Sample 宿主 README

| 项目 | 说明 |
|------|------|
| [MDKOSS.Cef.Sample](MDKOSS.Cef.Sample/README.md) | 通用 CEF，按 JSON 跑机型 |
| [MDKOSS.UI.WPF](MDKOSS.UI.WPF/README.md) | Prism + WPF 主界面 |
| [MDKOSS.Sample](MDKOSS.Sample/README.md) | 自定义设备 / MotionTask 扩展示例 |
| [MDKOSS.Sample.DieBonder](MDKOSS.Sample.DieBonder/README.md) | 半导体贴片机 |
| [MDKOSS.Sample.Dispenser](MDKOSS.Sample.Dispenser/README.md) | 三轴点胶机 |
| [MDKOSS.Sample.Pnp](MDKOSS.Sample.Pnp/README.md) | 拾取放置 |
| [MDKOSS.Sample.Modbus](MDKOSS.Sample.Modbus/README.md) | Modbus TCP Master Holding 联调 |
| [MDKOSS.Sample.Tools](MDKOSS.Sample.Tools/README.md) | 设备组件调试入口 |
| [MDKOSS.Tools.Calib](MDKOSS.Tools.Calib/README.md) | WPF 标定工具（各机型 Flow + 库内参数/结果） |
| [MDKOSS.Sample.Iec61131](MDKOSS.Sample.Iec61131/README.md) | Flow 工位节拍导出 IEC 61131 |

各宿主 / 扩展若带 `README.md`，以项目内说明为准。
