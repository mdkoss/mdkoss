# src/ — MDKOSS 源码

当前产品版本 **1.2.0**（见 `Directory.Build.props` / `MdkProduct.Version`）。更完整的上手说明见仓库根目录 [readme.md](../readme.md)；架构细项见 [docs/](../docs/README.md)。

## 分层

```text
宿主（Sample / Cef.Sample / Sample.DieBonder / Sample.Dispenser / Sample.Pnp / Config.Wpf）
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
| `MDKOSS.Drivers.Sim` / `Gts` / `Dmc` | 仿真 / 固高 / 雷赛驱动插件 |
| `MDKOSS.Extensions.Serial` / `Tcp` / `Mysql` / `Camera` / `PyScript` / `ModServer` | 可选设备扩展 |
| `MDKOSS.Cef` | CefSharp 界面库 + `views/` HMI |
| `MDKOSS.Cef.Extensions` | 主界面监控组态（控件扩展包 + `/api/hmi`） |
| `MDKOSS.Cef.Sample` | 通用 CEF 宿主（`startPage`=`index_hmi.html`） |
| `MDKOSS.Sample` | SampleExt 扩展示例宿主 |
| `MDKOSS.Sample.DieBonder` | 半导体贴片机 Demo |
| `MDKOSS.Sample.Dispenser` | 三轴点胶机 Demo |
| `MDKOSS.Sample.Pnp` | 拾取放置（PNP）Demo |
| `MDKOSS.Config.Wpf` | 离线配置编辑器 |

各宿主 / 扩展若带 `README.md`，以项目内说明为准。
