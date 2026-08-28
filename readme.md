# MDKOSS（Open Source Simplified Runtime）

`MDKOSS` 是从 `MDKSYS/mdkruntime` 提炼出的开源简化运行时。当前产品版本 **1.2.0**（`MdkProduct.Version` / `src/Directory.Build.props`），已形成可运行闭环：

- 配置加载（JSON -> Runtime）
- 驱动插件化（`IDriver` + `DriverFactory`；`sim` / `gts` / `dmc` 独立 DLL，运行时扫 `plugins/`）
- 设备组件体系（`gpio` / `vio` / `axis` / `platform`（XY…XYZUVW 多轴）/ `cameradev`；串口/TCP/相机等走 Extensions）
- 任务调度与心跳更新（`MTaskScheduler`）
- 变量中心（`MVarStore`）与 SQLite 持久化（排单 / 示教 / 配方）
- 基础监控界面（`HttpListener + HTML Dashboard`），含主界面 HMI、IO / 平台 / 串口等监控与调试页
- 桌面壳：**MDKOSS.Cef.Sample**（通用 CEF 宿主）、**MDKOSS.Sample**（扩展示例）、**MDKOSS.Sample.DieBonder**（贴片机）、**MDKOSS.Sample.Dispenser**（点胶机）、**MDKOSS.Sample.Pnp**（拾取放置）、**MDKOSS.Sample.Modbus**（Modbus IDriver 联调）、**MDKOSS.Config.Wpf**（WPF 配置）；**MDKOSS.Cef** 仅为 CefSharp 界面库

源码目录索引见 [src/README.md](./src/README.md)。

---

## Star History

<a href="https://www.star-history.com/?repos=mdkoss%2Fmdkoss&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=mdkoss/mdkoss&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=mdkoss/mdkoss&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=mdkoss/mdkoss&type=date&legend=top-left" />
 </picture>
</a>

---

## 架构文档

详细架构设计已整理至 **[docs/](./docs/README.md)**，包括分层结构、项目拆分、配置模型、扩展机制、监控 API、SQLite 持久化与 GUI 说明。下文 §2–§4 保留简要索引，完整内容以 docs 为准。

---

## 1. 当前设计目标

以最小依赖实现“可编译、可运行、可观测、可扩展”的运行时内核，保留 `mdkruntime` 的核心思想，去掉第一阶段非必需复杂度（桌面容器、复杂服务编排、Redis 同步等）。

### 1.1 已实现目标
- 可从 `configs/` 下第一个 JSON（默认路径 `MdkSetting.DefaultSettingsPath`）创建运行时实例
- 可按配置注册 Driver / Device / Task
- 可启动任务循环并更新变量状态
- 可通过监控接口获取运行时快照
- 可通过网页或 CEF 桌面壳查看 HMI / 监控页
- `RuntimeHost` + 各宿主 `Program` 完成配置加载、运行时 `Initialize/Start/Stop` 与日志（`AppLog` / NLog）

### 1.2 暂不纳入
- 完整 Nancy API 模块体系
- Redis 同步、热重载、插件热插拔

---

## 2. 项目结构（当前实现）

> 完整说明见 [docs/project-layout.md](./docs/project-layout.md)、[src/README.md](./src/README.md)。

```text
mdkoss/
├── MDKOSS.sln
├── readme.md
├── docs/                         # 架构与设计文档
├── tests/MDKOSS.Tests/
└── src/
    ├── README.md                 # 源码目录索引
    ├── MDKOSS.Core/              # 运行时内核（无内置板卡实现）
    ├── MDKOSS.Extensions/        # IMdkExtension 接入层
    ├── MDKOSS.Drivers.Sim/       # sim 驱动插件
    ├── MDKOSS.Drivers.Gts/       # gts 驱动插件
    ├── MDKOSS.Drivers.Dmc/       # 雷赛 DMC 驱动插件
    ├── MDKOSS.Extensions.Serial/ # serialdev
    ├── MDKOSS.Extensions.Tcp/    # tcpdev
    ├── MDKOSS.Extensions.Mysql/  # mysqldev
    ├── MDKOSS.Extensions.Camera/ # extcamera
    ├── MDKOSS.Extensions.PyScript/
    ├── MDKOSS.Extensions.ModServer/
    ├── MDKOSS.Cef/               # CefSharp 界面库 + views
    ├── MDKOSS.Cef.Sample/        # 通用 CEF 宿主 + configs
    ├── MDKOSS.Sample/            # SampleExt 扩展示例宿主 + configs
    ├── MDKOSS.Sample.DieBonder/  # 半导体贴片机 Demo 宿主 + configs
    ├── MDKOSS.Sample.Dispenser/  # 三轴点胶机 Demo 宿主 + configs
    ├── MDKOSS.Sample.Pnp/        # 拾取放置 Demo 宿主 + configs
    ├── MDKOSS.Sample.Modbus/     # Modbus IDriver 联调宿主 + configs
    ├── MDKOSS.Sample.Iec61131/   # 工位节拍 IEC 导出示例
    └── MDKOSS.Config.Wpf/        # WPF 配置宿主 + configs
```

宿主仅引用 Core + Extensions；构建时由 `MdkPlugins.targets` 将 Drivers / Extensions 复制到输出目录 `plugins/`，运行时 `DiscoverAndRegister()` 扫描加载。`configs` 与 `views` 会复制到可执行文件同级，默认从 `configs/` 下第一个 JSON 加载配置。

---

## 3. 模块职责

> 详见 [docs/core-subsystems.md](./docs/core-subsystems.md)、[docs/extensions.md](./docs/extensions.md)。

- 宿主与 CEF 界面：
  - **MDKOSS.Config.Wpf**：WPF 离线配置（`MainWindow`）与调试窗
  - **MDKOSS.Cef.Sample**：通用 CEF 宿主，按 JSON 跑机型，无业务硬编码
  - **MDKOSS.Sample**：SampleExt 扩展示例；嵌入 CEF；可选 `--console`
  - **MDKOSS.Sample.DieBonder**：半导体贴片机 Demo；嵌入 CEF；可选 `--console`
  - **MDKOSS.Sample.Dispenser**：三轴点胶机 Demo；嵌入 CEF；可选 `--console`
  - **MDKOSS.Sample.Pnp**：拾取放置 Demo；嵌入 CEF；可选 `--console`
  - **MDKOSS.Sample.Modbus**：Modbus IDriver Holding 联调；嵌入 CEF；可选 `--console`
  - **MDKOSS.Sample.Iec61131**：工位节拍 Flow → IEC 61131-3 导出
  - **MDKOSS.Cef**：CefSharp 界面库（`CefMainForm` / `views`），非可执行

  均在入口中先 `MdkSetting.Load`，再创建 `MdkRuntime`；Debug 构建启动时会清空当日日志文件（`AppLog`）。

- `src/MDKOSS.Core/core/mdk.cs`  
  Runtime Host。统一管理生命周期：`Initialize -> Start -> StopAsync -> Dispose`。内部完成变量、驱动、设备、任务的注册与编排。产品版本见 `MdkProduct`。

- `src/MDKOSS.Core/core/msetting.cs`  
  配置模型与加载器。定义 `DriverConfig`、`DeviceConfig`、`TaskConfig`，支持从 JSON 反序列化；`DefaultSettingsPath` 指向与程序同目录 `configs/` 下的第一个 JSON。

- `src/MDKOSS.Core/core/mdev.cs`  
  设备体系。包含设备基类 `MDeviceBase` 及 `GpioDevice` / `VioDevice` / `AxisDevice` / `PlatformDevice` / `CameraDevDevice` 等。`PlatformDevice` 由多条 `AxisDevice` 组成，轴布局由 `MPlatformKind`（XY、XYZ、XYZU、XYZUV、XYZUVW）描述。串口等通信设备在独立扩展项目中实现。

- `src/MDKOSS.Extensions.Serial/`  
  串口设备（`serialdev`）。打开/关闭端口、波特率等参数、文本与二进制读写；HTTP 模块挂到监控服务。

- `src/MDKOSS.Core/core/gpio_device_parameters.cs`  
  解析 GPIO 的 `in.*` / `out.*` 与可选 `driverIds` 作用域。

- `src/MDKOSS.Core/core/platform_device_parameters.cs`  
  解析平台设备 `kind`、类型简写（`xy` / `xyz` / …）及按轴 `axis.X` 等驱动绑定。

- `src/MDKOSS.Core/core/vio_device_parameters.cs`  
  解析虚拟 GPIO（`vio`）的 `in.*` / `out.*`：取值须为空或 `virtual`，禁止物理 `driverId:address` 路由。

- `src/MDKOSS.Core/core/mtask.cs`  
  任务体系。包含 `MTaskBase` 和 `MTaskScheduler`，并提供基础任务 `PollDriverTask` 用于驱动心跳监控。

- `src/MDKOSS.Core/core/mvar.cs`  
  线程安全变量中心。提供 `Set/Get/TryGet/Snapshot`，用于模块间状态共享与监控导出。

- `src/MDKOSS.Core/core/drivers/idriver.cs` + `driver_factory.cs`  
  驱动统一接口与工厂；具体实现不在 Core 内，由插件 Bootstrap 注册 `type`。

- `src/MDKOSS.Drivers.Sim` / `Gts` / `Dmc`  
  仿真、固高、雷赛板卡插件（`DrvSim` / `DrvGts` / `DrvDmc`），构建产物进入 `plugins/`。

- `src/MDKOSS.Core/server/monitoringserver.cs`  
  轻量监控服务，提供主界面、IO/平台/串口等监控与调试页，以及 `/api/status`、`/api/devices`、`/api/io/write` 等；串口等扩展 API 由对应 Extensions HTTP 模块注册。详见 [docs/monitoring-api.md](./docs/monitoring-api.md) 与 `src/MDKOSS.Cef/views/README.md`。

- `src/MDKOSS.Core/server/*page.cs`  
  从输出目录旁 `views/` 加载对应 HTML（`index` / `monitor_*` / `debug_*` 等）。

- `src/MDKOSS.Core/core/mdk.cs`（`TryWriteDigitalOutput`）  
  供监控 HTTP 调用：在已注册设备中查找 `GpioDevice` 或 `VioDevice`，按别名执行 `WriteOutput`。

---

## 4. 运行时架构与数据流

> 详见 [docs/architecture.md](./docs/architecture.md)。

### 4.1 分层架构

1. **Configuration Layer**：`MdkSetting`
2. **Runtime Orchestration Layer**：`MdkRuntime`
3. **Driver Layer**：`IDriver` / `DriverFactory` + 插件（`DrvSim` / `DrvGts` / `DrvDmc`）
4. **Device Layer**：`MDeviceBase` + 设备子类（通信类走 Extensions）
5. **Task Layer**：`MTaskScheduler` + `MTaskBase`
6. **State Layer**：`MVarStore` + SQLite DataStore
7. **Monitoring Layer**：`MonitoringServer` + views HMI / REST API

### 4.2 启动顺序

1. 读取配置（`MdkSetting.Load`，默认文件为 `configs/` 下第一个 JSON）
2. `MdkRuntime.Initialize()`：
   - Seed Vars
   - 初始化 Drivers
   - 初始化 Devices
   - 注册 Tasks
3. `MdkRuntime.Start()`：
   - 启动 Devices
   - 启动 Scheduler
4. 启动监控服务（默认 `http://127.0.0.1:5080/`，浏览器也可用 `http://localhost:5080/`）

### 4.3 停止顺序

1. `MdkRuntime.StopAsync()`：
   - 先停 Task Scheduler
   - 再停 Devices
2. `Dispose()`：
   - 释放 Drivers / Devices / Scheduler

---

## 5. 配置模型（当前版本）

`configs/sample.setting.json`（随构建复制到输出目录）包含如下核心字段：

- `projectName`：项目名称（区分不同机型/工程）
- `startPage`（可选）：CEF/监控首页，如 `indexDieBonder.html` / `indexPnp.html`；缺省 `index.html`
- `monitoringPrefix`（可选）：监控 HTTP 监听地址，必须以 `/` 结尾，例如 `http://127.0.0.1:5081/`。默认 `http://127.0.0.1:5080/`（并自动登记 `localhost` 别名，不登记 `[::1]` 以避免 Windows http.sys 冲突）。端口被占用时请改此项。
- `cycleMs`：主循环周期（预留）
- `drivers[]`：
  - `id` / `type` / `enabled` / `parameters`
- `devices[]`：  
  - `id` / `name` / `type` / `driverId` / `enabled` / `parameters`
  - `gpio`：`parameters` 中 `in.*` / `out.*` 将别名映射到 `driverId:address`；可选 `driverIds`（逗号分隔）限定本设备可见的驱动子集；快照中 `gpioIoPoints`（别名、方向、驱动、地址、在线、当前读值）。
  - `vio`（虚拟 GPIO）：单驱动（通常为 `sim`）。`in.*` / `out.*` 的取值须为空或 `virtual`；运行时地址为 `vio.{deviceId}.in|out.{alias}`，读写走该驱动的内存语义。快照仍使用 `gpioIoPoints` 形状便于监控表格复用。
  - `platform`：多轴平台。可将 `type` 设为 `platform` 并在 `parameters.kind` 中写 `xy` / `xyz` / `xyzu` / `xyzuv` / `xyzuvw`，或将 `type` 直接设为上述简写之一。未写 `kind` 且 `type` 为 `platform` 时默认 `xyz`。每轴一个 `AxisDevice`；可用顶层 `driverId` 作为所有轴的默认驱动，或用 `axis.X`、`axis.Y` 等按轴指定驱动 id。快照中 `driverType` 形如 `platform-xyz`，并带 `platformAxes`（轴字母、驱动 id、在线）。
  - `serialdev`：串口设备。`parameters` 支持配置 `portName`、`baudRate`、`dataBits`、`parity`、`stopBits`、`readTimeout`、`writeTimeout`、`dtrEnable`、`rtsEnable`。快照中 `serialPortInfo` 包含端口状态与配置信息。
  - `axis` / `cameradev`：单驱动设备，依赖 `driverId`。
- `tasks[]`：
  - `name` / `driverId` / `intervalMs` / `parameters`
- `vars`：初始变量字典

支持设备类型（`type` 字段，大小写不敏感）：
- `gpio`：多驱动物理 IO 路由
- `vio`：单驱动虚拟 IO（见上）
- `axis`
- `platform`：另支持 `xy` / `xyz` / `xyzu` / `xyzuv` / `xyzuvw` 作为与 `kind` 等价的 `type` 简写
- `serialdev`：串口通信设备（RS-232C）
- `cameradev`  

`configs/sample.setting.json` 中已包含上述类型的示例条目，便于本地对照与联调。

---

## 6. 监控能力

运行后可访问（端口以 `monitoringPrefix` 为准，默认可用 `127.0.0.1` 或 `localhost`）：

- 综合监控页：`http://127.0.0.1:5080/`
- IO 监控页：`http://127.0.0.1:5080/monitor_io.html`（左侧 DI、右侧 DO；各列表支持关键词筛选）
- 串口调试页：`http://127.0.0.1:5080/debug_serial.html`（端口配置、文本/十六进制收发、实时状态监控）
- 平台步进示教：`http://127.0.0.1:5080/debug_platform.html?deviceId={platformId}`（步进点动、使能、示教点）
- 平台只读监控：`http://127.0.0.1:5080/monitor_platform.html?deviceId={platformId}`
- 运行时总览：`http://127.0.0.1:5080/monitor_runtime.html`
- 快照接口：`http://127.0.0.1:5080/api/status`

### 6.1 API 端点

| 路由 | 方法 | 功能 |
|------|------|------|
| `/api/status` | GET | 运行时快照 |
| `/api/devices` | GET | 列出所有设备 |
| `/api/devices/{id}` | GET | 获取单个设备详情 |
| `/api/devices/{id}/action` | POST | 执行设备操作 |
| `/api/io/write` | POST | 写入数字输出（gpio/vio） |
| `/api/serial/status` | GET | 串口状态 |
| `/api/serial/open` | POST | 打开串口 |
| `/api/serial/close` | POST | 关闭串口 |
| `/api/serial/write` | POST | 发送文本 |
| `/api/serial/writeBin` | POST | 发送二进制 |
| `/api/serial/read` | POST | 读取数据 |
| `/api/serial/discard` | POST | 清空缓冲区 |

`/api/status` 返回：
- `Version`（`MdkProduct.Version`，当前 1.2.0）
- `ProjectName`
- `IsRunning`
- `Drivers`
- `Devices`（`gpio` / `vio` 含 `gpioIoPoints`；`platform` 另含 `platformAxes`；`serialdev` 含 `serialPortInfo`）
- `Vars`

该快照来自 `MdkRuntime.GetSnapshot()`，可直接用于后续扩展 API / WebSocket / 历史存储。

---

## 7. 本地运行

**环境**：Windows x64；CEF 模式需安装 [Visual C++ 2019+ 可再发行组件](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)。主工程目标为 `win-x64`，输出在各自项目目录 `bin/{Configuration}/net8.0-windows10.0.22621.0/win-x64/`（含 `plugins/`）。

在仓库根目录构建与测试：

```bash
dotnet build MDKOSS.sln -c Release
dotnet test MDKOSS.sln -c Release --no-build
```

WPF 配置界面：

```bash
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj
```

CEF 宿主：

```bash
# 通用 CEF（按 sample.setting.json）
dotnet run --project src/MDKOSS.Cef.Sample/MDKOSS.Cef.Sample.csproj

# DieBonder
dotnet run --project src/MDKOSS.Sample.DieBonder/MDKOSS.Sample.DieBonder.csproj

# 三轴点胶机
dotnet run --project src/MDKOSS.Sample.Dispenser/MDKOSS.Sample.Dispenser.csproj

# PNP
dotnet run --project src/MDKOSS.Sample.Pnp/MDKOSS.Sample.Pnp.csproj

# Modbus IDriver 联调（Holding 默认 200 字）
dotnet run --project src/MDKOSS.Sample.Modbus/MDKOSS.Sample.Modbus.csproj

# SampleExt 扩展示例
dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj
```

也可使用根目录脚本 `run-src-mdkoss.bat` / `run-src-mdkoss-cef.bat`。

控制台模式（无 GUI，使用默认配置文件）：

```bash
dotnet run --project src/MDKOSS.Sample.DieBonder/MDKOSS.Sample.DieBonder.csproj -- --console
```

默认配置路径为可执行文件目录下 `configs/` 中的第一个 JSON。日志目录为输出目录下的 `logs/yyyyMMdd.log`。

控制台模式看到如下输出表示启动成功：

- `MDKOSS runtime started.`
- `Monitor UI: http://127.0.0.1:5080/`（或 `http://localhost:5080/`）

CEF / WPF 模式请查看 `logs/` 与窗体；浏览器亦可直接访问上述 Monitor URL。

---

## 8. 下一步建议

近期已补齐：产品版本 1.2.0（`MdkProduct` / GitHub Release）、驱动与扩展插件化（`plugins/`）、宿主 **Cef.Sample** / **Sample** / **Sample.DieBonder** / **Sample.Dispenser** / **Sample.Pnp** / **Sample.Modbus** / **Config.Wpf**、HMI `views/` 与监控 API、SQLite 排单/配方、流程任务、`tests/MDKOSS.Tests` 与 GitHub Actions。

可选后续方向：

- 为 axis / cameradev 等补充与 JSON 对齐的强类型参数块（GPIO / platform / vio 已分文件解析）
- 监控 HTTP 鉴权、HTTPS、或 WebSocket 推送快照
- 更完整的错误码分层与本地化文案

---

## 9. 与 mdkruntime 的关系

- `MDKOSS` 是 `mdkruntime` 的开源简化实现，不追求功能全量对齐；
- 继承核心设计原则：配置驱动、设备抽象、任务调度、状态中心、运行态可观测；
- 目标是作为轻量内核和教学/验证基线，后续按需渐进扩展。  

---

## 10. 开源许可与治理

本仓库采用 **MIT License** 开源，允许商业使用、修改、分发和再授权。

为保证协作透明和社区治理，新增以下文档：

- `LICENSE`：MIT 许可全文
- `CONTRIBUTING.md`：贡献流程与协作规范
- `CODE_OF_CONDUCT.md`：社区行为准则
- `SECURITY.md`：安全漏洞披露流程
- `RELEASE_NOTES.md`：初始版本与后续发布说明

如需对外发布，建议在仓库主页（如 GitHub About）同步标注许可证为 `MIT`。

### 10.1 English Open Source Notice

This repository is released under the **MIT License**, allowing commercial use,
modification, distribution, private use, and sublicensing.

To support transparent collaboration and community governance, the following
documents are included:

- `LICENSE`: Full MIT license text
- `CONTRIBUTING.md`: Contribution workflow and collaboration guidelines
- `CODE_OF_CONDUCT.md`: Community code of conduct
- `SECURITY.md`: Security vulnerability disclosure process
- `RELEASE_NOTES.md`: Initial and future release notes

For public release, it is recommended to also mark the repository license as
`MIT` in your hosting platform metadata (for example, GitHub About).

