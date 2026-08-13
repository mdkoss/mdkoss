# MDKOSS.Cef.Sample

轻量 CEF 宿主，专门打开 [`MDKOSS.Cef/views/index.html`](../MDKOSS.Cef/views/index.html) 联调：

- 核心 HMI：`popup_*` / `monitor_*` / `debug_*` / `man_*`
- 扩展设备：配置实例 + 对应 debug 页 / REST，便于点选加载与调试

**不是全功能 / 机型 Sample。** 不加载 DieBonder、PNP tray/任务等机型页与流程；真机驱动（GTS/DMC）与桌面配置器见专用工程。

**views 复用**：本项目不复制/不 Link `MDKOSS.Cef/views`；通过 `ProjectReference` 引用 `MDKOSS.Cef`，构建时 Content 直接落到输出目录的 `views/`（避免再 Link 造成 `views\views` 嵌套）。

## 运行

```bash
dotnet run --project src/MDKOSS.Cef.Sample/MDKOSS.Cef.Sample.csproj -c Debug
```

或 Visual Studio 将 `MDKOSS.Cef.Sample` 设为启动项目。

默认监控地址：`http://127.0.0.1:5081/`（与 DieBonder Sample 的 5080 错开）。

浏览器也可直接访问同一前缀下的页面，例如：

- http://127.0.0.1:5081/index.html
- http://127.0.0.1:5081/monitor_runtime.html
- http://127.0.0.1:5081/debug_axis.html
- http://127.0.0.1:5081/debug_serial.html
- http://127.0.0.1:5081/debug_db.html
- http://127.0.0.1:5081/debug_camera.html
- http://127.0.0.1:5081/man_device.html

## 配置

[`configs/sample.setting.json`](configs/sample.setting.json) 用仿真驱动挂一份**设备类型最小集**，让 debug / `/api/*` 有对象可点：

| 类别 | 实例 | 调试入口 |
|------|------|----------|
| 驱动 | `sim1`（`sim`） | `debug_driver` |
| GPIO / 相机占位 | `gpio1`、`cam1`（`cameradev`） | `debug_io` / `monitor_camera` |
| 串口 | `serial1`（`serialdev`） | `debug_serial`、`/api/serial`（不自动 Open） |
| TCP | `tcp1`（`tcpdev`） | `/api/tcp`（不自动 Connect） |
| Modbus | `mod-1` + `modc-1` | `/api/modserver`（Server 监听 `127.0.0.1:1502`） |
| Python | `py-1`（`devpyscript`） | `/api/pyscript`（脚本 `configs/scripts/hello.py`） |
| 扩展相机 | `cam-ext-1`（`extcamera`） | `debug_camera`、`/api/extcamera` |
| 视觉 | `vision-1` + `visions[]` | 设备 action `captureAndRun`；变量 `vision.*` |
| 轴 / 平台 | X/Y/Z + 旋转 U；`xy` / `xyz` / `xyzu` | `debug_platform` 多轴示教 |
| 数据库 | `databasePath` = `data/mdk.db` | `debug_db`、`/api/db`（启动时自动建库） |
| 报警 | `alarms[]`：`alm-demo`（`alarm.test`）/ `alm-warn` | `popup_alarms`、`/api/alarms`（测试触发 / 复位） |

可用 `--setting path\to\file.json` 覆盖。

串口 / TCP 默认不连真端口：打开 `debug_serial` 或调 `/api/tcp` 时再连。Modbus Client 会连本进程 Server。`devpyscript` 需要本机 `python` 在 PATH 上。

## 覆盖边界

本 Sample **演示**核心 HMI 壳 + 扩展设备的配置加载与调试 API。

**不演示（请用专用工程）：**

| 能力 | 工程 |
|------|------|
| DieBonder 机型 HMI / bond 任务 | `src/MDKOSS.Sample` |
| Tray / PNP 循环与 `indexPnp` | `examples/pnp` |
| GTS / DMC 真机卡 | 对应 Drivers 工程（本 Sample 仅 sim） |
| 桌面配置器 | `MDKOSS.Config.Wpf` |

关于页里的 PNP 链接指向机型静态页，**没有本 Sample 的 tray/任务配置**，打开后无业务数据。

## 依赖

- `MDKOSS.Core` / `MDKOSS.Extensions` / `MDKOSS.Cef`
- 构建导入 `MdkPlugins.targets`，输出 `plugins/` 含：
  - Drivers：`Sim`、`Gts`、`Dmc`（配置只用 `sim`）
  - Extensions：`Serial`、`Tcp`、`Camera`、`PyScript`、`ModServer`
  - Machine：`Pnp`（DLL 会复制，本 Sample **不启用** tray/PNP 任务）
