# MDKOSS.Cef.Sample 功能覆盖分析

- 日期：2026-08-13
- 对象：`src/MDKOSS.Cef.Sample`
- 结论：作为**核心 CEF HMI + 扩展设备配置/调试宿主**够用；**不是全功能 / 机型 Sample**。DieBonder、PNP tray/任务、GTS/DMC 真机、Config.Wpf 仍由专用工程演示。

---

## 1. Sample 定位（设计意图）

`Program.cs` / `README.md`：轻量 CEF 宿主，强制 `startPage = index.html`，联调核心 HMI，并挂一份扩展设备最小集供 debug / API 点选。**不加载机型页与机型流程。**

| 项 | 实际 |
|---|---|
| 宿主 | WinForms + CefSharp（`CefMainForm`） |
| 入口页 | `index.html`（`MDKOSS.Cef/views`，经 ProjectReference 复制到输出） |
| 配置 | `configs/sample.setting.json`，监控前缀 `http://127.0.0.1:5081/` |
| 扩展发现 | `MdkExtensionHost.DiscoverAndRegister` |
| 插件构建 | 导入 `MdkPlugins.targets`（构建后复制到 `plugins/`） |

与 `MDKOSS.Sample`（DieBonder，5080）/ `examples/pnp` 分工：Cef.Sample 负责**通用 HMI 壳 + 设备类型演示**，机型工程负责**业务场景**。

---

## 2. 当前配置实际启用的能力

`sample.setting.json` 中已实例化：

| 类别 | 内容 | 可演示程度 |
|---|---|---|
| 驱动 | `sim1`（type=`sim`） | 仿真 IO / 轴 |
| GPIO / 核心相机 | `gpio1`、`cam1`（`cameradev`） | 点位 + 占位相机 |
| 扩展设备 | `serial1`、`tcp1`、`mod-1`/`modc-1`、`py-1`、`cam-ext-1` | debug / REST 有对象；串口/TCP 不自动连端口 |
| 视觉 | `vision-1` + `visions[]`（inspect / pass） | 设备 action + `vision.*` 变量 |
| 轴 | `axis-x/y/z`（linear）+ `axis-u`（rotary） | 单轴监控 |
| 平台 | `xy` / `xyz` / `xyzu` | 多轴示教 |
| 任务 | `cycle` / `operation` / `pollDriver` | 主界面启停、灯、轮询 |
| 数据库 | `databasePath` = `data/mdk.db` | `debug_db` / `/api/db`（启动建库） |
| 报警 | 变量 `alarm.test` / `alarm.warn` + `alarms[]` | `popup_alarms`、`/api/alarms` |
| 排单 / 工单 | `rcp-fast` / `rcp-safe`、`order.list` | 主界面 |

由此可覆盖：

- 主界面：订单、启停复位、排单、状态灯、顶部 popup
- 监控：`monitor_runtime` / `io` / `platform` / `axis` / `camera` / `task`
- 调试：`debug_platform`（含 xyz/xyzu）、`axis` / `io` / `camera` / `serial` / `db` / `driver` / `machine`
- 配置页：`man_*`（驱动/设备/轴/平台/GPIO/排单/任务）

---

## 3. HMI 页面可达性

`MonitoringServer` 注册完整核心静态页；`popup_about.html` 链到 `monitor_*` / `debug_*` / `man_*`，PNP 链接标明需 `examples/pnp` 完整配置。

| 页面族 | 是否随 CEF views 落地 | 在本 Sample 中是否有数据可玩 |
|---|---|---|
| `index.html` + `popup_*` | 是 | 是（含设备列表链到对应 debug） |
| `monitor_*` | 是 | 是（仿真资源） |
| `debug_serial` / `debug_db` / `debug_camera` | 是 | 是（有 `serial1` / db 路径 / `extcamera`） |
| `debug_platform` | 是 | 是（xy / xyz / xyzu） |
| `man_*` | 是 | 是 |
| `indexPnp.html` / `monitorPnp.html` | PNP 插件 StaticPage | **刻意不配**：无 tray / pnp 任务 |

---

## 4. 插件与 API：已加载且已配置（机型插件除外）

构建输出 `plugins/`（`MdkPlugins.targets` 全量复制）：

- Drivers：Sim、Gts、Dmc
- Extensions：Serial、Tcp、Mysql、Camera、PyScript、ModServer
- Machine：Pnp（DLL 在，本 Sample **不启用** tray/任务）

| 插件 | 设备 type | Monitoring API | Sample 配置 |
|---|---|---|---|
| Sim | driver `sim` / `vio` | 核心 `/api/io` 等 | **仅 sim** |
| Serial | `serialdev` | `/api/serial` | `serial1`（手动 Open） |
| Tcp | `tcpdev` | `/api/tcp` | `tcp1`（手动 Connect） |
| Mysql | `mysqldev` | `/api/mysql` | `mysql1`（手动 Connect） |
| Camera ext | `extcamera` | `/api/extcamera` | `cam-ext-1`（另保留核心 `cam1`） |
| PyScript | `devpyscript` | `/api/pyscript` | `py-1` + `configs/scripts/hello.py` |
| ModServer | `devmodserver` / `devmodclient` | `/api/modserver` | `mod-1` 监听 `127.0.0.1:1502`，`modc-1` 自动连 |
| Gts / Dmc | driver `gts` /（Dmc 包装未完成） | — | 否（真机） |
| Pnp | `tray` + pnp 任务 | `/api/pnp` + 静态页 | 否（机型） |

核心 Monitoring API（始终挂载）：

- `/api/status` `/api/io` `/api/devices` `/api/recipe` `/api/orders` `/api/teach` `/api/db` `/api/alarms` `/api/visions` `/api/config` `/api/tasks` `/api/task`

---

## 5. 覆盖矩阵（相对整仓能力）

### 5.1 覆盖较好（建议视为「已演示」）

- CEF 宿主生命周期：初始化 / 监控前缀 URL / 关闭清理
- 运行时引导：`MdkSetting` → `MdkRuntime` Initialize/Start
- 插件发现机制（DLL 扫描注册）
- 仿真驱动 + GPIO + 线性/旋转轴 + XY/XYZ/XYZU 平台 + 核心/扩展相机
- 扩展设备最小集：serial / tcp / modbus server+client / pyscript / extcamera
- 视觉配方 2 条 + `visiondev`
- SQLite `data/mdk.db` + `debug_db`
- 报警定义 2 条 + `popup_alarms` / `/api/alarms`（测试触发）
- 基础任务：`cycle` / `operation` / `pollDriver`
- 排单、工单变量、主界面启停
- 核心 HMI 四族页面导航
- 核心 REST + 扩展 REST（有设备实例）

### 5.2 部分覆盖 / 依赖本机环境

- 串口 Open 需要本机 COM 或虚拟串口
- TCP Connect 需要本机有监听（默认 `127.0.0.1:15100`）
- `devpyscript` 需要 PATH 上的 `python`
- PNP 入口链接仍可打开，但无 tray/任务数据（已在 about 标注）

### 5.3 明确未演示（需其他工程）

| 能力 | 何处有 | Cef.Sample |
|---|---|---|
| DieBonder 机型 HMI / bond 任务 / `/api/bond` | `MDKOSS.Sample` | 刻意不做 |
| Tray / PNP 循环与监控 | `examples/pnp` | 刻意不做 |
| 真机驱动 GTS / DMC | Drivers.* | 仅仿真 |
| VIO 驱动别名实例 | Sim 注册 `vio` | 未配 |
| Console 无 UI 模式 | `RuntimeHost.RunConsoleRuntimeAsync` | 无 `--console` |
| 桌面配置器 | `MDKOSS.Config.Wpf` | 独立工具 |

---

## 6. 总评

| 问题 | 答案 |
|---|---|
| 是否足够演示**所有**功能？ | **否**（机型 / 真机 / 配置器不在范围） |
| 是否足够演示**核心 CEF HMI + 扩展设备配置与调试**？ | **是** |
| 与 DieBonder / PNP Sample 关系 | 互补：通壳+设备类型 vs 机型场景 |

覆盖粗估（按「用户能在界面或 API 上点到有意义对象」计）：

- 核心 HMI / 仿真运动与 IO：**约 80%**
- 扩展设备与协议面：**约 70%**（实例在；真端口/Python 视本机）
- 机型与业务场景：**0%**（刻意）
- 配置工具 / Console / 真机卡：**不在本 Sample 范围**

---

## 7. 增量项落实情况

1. **扩展设备最小集**：已加 `serialdev` / `tcpdev` / `devmodserver` + `devmodclient` / `devpyscript` / `extcamera`。
2. **数据库**：`databasePath` = `data/mdk.db`。
3. **视觉与报警**：2 条 `visions` + `visiondev`；2 条 `alarms`（`alarm.test` / `alarm.warn`）+ `/api/alarms`。
4. **PNP**：不加 tray；about 链接标明需完整 PNP 配置。
5. **平台族**：额外 `xyz` / `xyzu`（仍用 sim）。
6. **文档**：README 插件列表与覆盖边界已更新。
7. **未塞入**：DieBonder、GTS/DMC 真机、Config.Wpf。

---

## 8. 关键源文件

- `src/MDKOSS.Cef.Sample/Program.cs`
- `src/MDKOSS.Cef.Sample/README.md`
- `src/MDKOSS.Cef.Sample/configs/sample.setting.json`
- `src/MDKOSS.Cef.Sample/configs/scripts/hello.py`
- `src/MDKOSS.Cef/views/index.html` / `popup_about.html` / `popup_devices.html` / `popup_alarms.html`
- `src/MDKOSS.Core/server/monitoringserver.cs`
- `src/MdkPlugins.targets`
- 对照：`src/MDKOSS.Sample/`、`examples/pnp/`
