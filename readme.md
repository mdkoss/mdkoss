# MDKOSS（Open Source Simplified Runtime）

`MDKOSS` 是从 `MDKSYS/mdkruntime` 提炼出的开源简化运行时。当前版本已形成可运行闭环：

- 配置加载（JSON -> Runtime）
- 驱动抽象与实例化（`IDriver` + `DrvGts` / `DrvSim`）
- 设备组件体系（`gpio` / `vio` / `axis` / `platform`（XY…XYZUVW 多轴）/ `serialdev` / `cameradev`）
- 任务调度与心跳更新（`MTaskScheduler`）
- 变量中心（`MVarStore`）
- 基础监控界面（`HttpListener + HTML Dashboard`），含 **主界面 HMI**（`index.html`）、**IO 专用页**（DI/DO 分栏、筛选、DO 写入）、**串口调试页**
- 桌面壳：**WinForms 监控** 或 **CefSharp 嵌入式浏览器**（`--cef`，加载 `views/index.html`）

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

## 1. 当前设计目标

以最小依赖实现“可编译、可运行、可观测、可扩展”的运行时内核，保留 `mdkruntime` 的核心思想，去掉第一阶段非必需复杂度（桌面容器、复杂服务编排、Redis 同步等）。

### 1.1 已实现目标
- 可从 `configs/sample.setting.json`（默认路径 `MdkSetting.DefaultSettingsPath`）创建运行时实例
- 可按配置注册 Driver / Device / Task
- 可启动任务循环并更新变量状态
- 可通过监控接口获取运行时快照
- 可通过网页或 CEF 桌面壳查看 HMI / 监控页
- `Program.cs` 统一完成配置加载、运行时 `Initialize/Start/Stop` 与日志（`AppLog` / NLog）

### 1.2 暂不纳入
- 完整 Nancy API 模块体系
- Redis 同步、热重载、插件热插拔

---

## 2. 项目结构（当前实现）

```text
mdkoss/
├── MDKOSS.sln
├── readme.md
├── tests/
│   └── MDKOSS.Tests/
└── src/
    ├── MDKOSS.csproj
    ├── Program.cs
    ├── configs/
    │   └── sample.setting.json
    ├── views/
    │   ├── index.html
    │   ├── monitoringpage.html
    │   ├── monitorIO.html
    │   ├── monitorPlatform.html
    │   ├── monitorPlatform.js
    │   ├── motiorplatform.md
    │   └── debugserialdev.html
    ├── tasks/
    ├── extensions/
    │   ├── serialdev.cs
    │   └── tcpdev.md
    ├── gui/
    │   ├── winform/
    │   └── cef/
    │       ├── CefMainForm.cs
    │       └── CefRuntimeBootstrap.cs
    └── core/
        ├── mdk.cs
        ├── msetting.cs
        ├── mdev.cs
        ├── mtask.cs
        ├── mvar.cs
        ├── serial_device_parameters.cs
        ├── drivers/
        │   ├── idriver.cs
        │   ├── driver_factory.cs
        │   ├── drvgts.cs
        │   └── drvsim.cs
        ├── gpio_device_parameters.cs
        ├── platform_device_parameters.cs
        ├── vio_device_parameters.cs
        ├── mdk_errors.cs
        ├── runtime_task_factory.cs
        ├── monitor/
        │   ├── monitoringserver.cs
        │   ├── monitoringpage.cs
        │   ├── monitoriopage.cs
        │   ├── monitorplatformpage.cs
        │   ├── debugserialdevpage.cs
        │   └── indexpage.cs
```

构建后，`configs` 与 `views` 会复制到输出目录（与可执行文件同级），运行时默认从 `configs/sample.setting.json` 加载配置。

---

## 3. 模块职责

- `Program.cs`  
  应用入口，支持三种 UI 模式：
  - **默认 / `--winform`**：WinForms 监控窗（`MainForm`），运行时由 `Main` 加载配置并 `Initialize → Start`，关闭窗体后 `StopAsync`
  - **`--cef`**：CefSharp 桌面壳（`CefMainForm`），打开输出目录下 `views/index.html`（HMI 导航至各监控页）
  - **`--console`**：无 GUI，仅后台运行时 + HTTP 监控服务  

  桌面模式均在 `Main` 中先 `MdkSetting.Load`，再创建 `MdkRuntime`；Debug 构建启动时会清空当日日志文件（`AppLog`）。

- `src/core/mdk.cs`  
  Runtime Host。统一管理生命周期：`Initialize -> Start -> StopAsync -> Dispose`。内部完成变量、驱动、设备、任务的注册与编排。

- `src/core/msetting.cs`  
  配置模型与加载器。定义 `DriverConfig`、`DeviceConfig`、`TaskConfig`，支持从 JSON 反序列化；`DefaultSettingsPath` 指向与程序同目录的 `configs/sample.setting.json`。

- `src/core/mdev.cs`
  设备体系。包含设备基类 `MDeviceBase` 及 `GpioDevice` / `VioDevice` / `AxisDevice` / `PlatformDevice` / `SerialDevice` / `CameraDevDevice` 子类。`PlatformDevice` 由多条 `AxisDevice` 组成，轴布局由 `MPlatformKind`（XY、XYZ、XYZU、XYZUV、XYZUVW）描述。

- `src/extensions/serialdev.cs`
  串口设备（`SerialDevice`）。提供 RS-232C 串口通信能力：打开/关闭端口、配置参数（波特率/数据位/校验位/停止位）、文本与二进制读写、缓冲区管理。

- `src/core/gpio_device_parameters.cs`  
  解析 GPIO 的 `in.*` / `out.*` 与可选 `driverIds` 作用域。

- `src/core/platform_device_parameters.cs`  
  解析平台设备 `kind`、类型简写（`xy` / `xyz` / …）及按轴 `axis.X` 等驱动绑定。

- `src/core/vio_device_parameters.cs`  
  解析虚拟 GPIO（`vio`）的 `in.*` / `out.*`：取值须为空或 `virtual`，禁止物理 `driverId:address` 路由。

- `src/core/mtask.cs`  
  任务体系。包含 `MTaskBase` 和 `MTaskScheduler`，并提供基础任务 `PollDriverTask` 用于驱动心跳监控。

- `src/core/mvar.cs`  
  线程安全变量中心。提供 `Set/Get/TryGet/Snapshot`，用于模块间状态共享与监控导出。

- `src/core/drivers/idriver.cs`  
  驱动统一接口，约束驱动初始化、连接状态、读写行为。

- `src/core/drivers/drvgts.cs`  
  GTS 示例驱动（内存映射模拟），用于本地联调和端到端流程验证。

- `src/core/drivers/drvsim.cs`  
  软件仿真驱动：内存键值、DI/DO、轴运动等，常用于无硬件开发与 `vio` 虚拟 IO。

- `src/core/monitor/monitoringserver.cs`
  轻量监控服务，提供：
  - `GET /`、`GET /index.html`：主界面（HMI）
  - `GET /monitorIO.html`：IO 监控页（DI/DO 分栏、本地筛选、DO 拨动写入）
  - `GET /debugSerialDev.html`：串口调试页（端口配置、文本/十六进制收发、实时状态）
  - `GET /monitorPlatform.html`：平台步进示教页（`PlatformDevice` 点动、位置监视、示教点 localStorage）
  - `GET /api/status`：运行时快照 JSON
  - `POST /api/io/write`：写入数字输出（仅 `gpio` / `vio` 设备）
  - `GET /api/devices`：列出所有设备
  - `GET /api/devices/{id}`：获取单个设备详情
  - `POST /api/devices/{id}/action`：执行设备操作
  - `GET /api/serial/status?deviceId=xxx`：串口状态
  - `POST /api/serial/open`：打开串口
  - `POST /api/serial/close`：关闭串口
  - `POST /api/serial/write`：发送文本
  - `POST /api/serial/writeBin`：发送二进制
  - `POST /api/serial/read`：读取数据

- `src/core/monitor/monitoringpage.cs`  
  从输出目录旁 `views/monitoringpage.html` 加载综合监控页 HTML。

- `src/core/monitor/monitoriopage.cs`  
  从 `views/monitorIO.html` 加载 IO 监控页 HTML（与上相同复制规则）。

- `src/core/monitor/monitorplatformpage.cs`  
  从 `views/monitorPlatform.html` 加载平台步进示教页（设计说明见 `views/motiorplatform.md`）。

- `src/core/monitor/indexpage.cs`  
  从 `views/index.html` 加载主界面 HTML。

- `src/core/monitor/debugserialdevpage.cs`  
  从 `views/debugserialdev.html` 加载串口调试页 HTML。

- `src/core/mdk.cs`（`TryWriteDigitalOutput`）  
  供监控 HTTP 调用：在已注册设备中查找 `GpioDevice` 或 `VioDevice`，按别名执行 `WriteOutput`。

---

## 4. 运行时架构与数据流

### 4.1 分层架构

1. **Configuration Layer**：`MdkSetting`
2. **Runtime Orchestration Layer**：`MdkRuntime`
3. **Driver Layer**：`IDriver` / `DrvGts` / `DrvSim`
4. **Device Layer**：`MDeviceBase` + 设备子类
5. **Task Layer**：`MTaskScheduler` + `MTaskBase`
6. **State Layer**：`MVarStore`
7. **Monitoring Layer**：`MonitoringServer` + `MonitoringPage` + `MonitorIoPage`

### 4.2 启动顺序

1. 读取配置（`MdkSetting.Load`，默认文件为 `configs/sample.setting.json`）
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

- `projectName`：项目名称
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
- IO 监控页：`http://127.0.0.1:5080/monitorIO.html`（左侧 DI、右侧 DO；各列表支持关键词筛选；DO 在驱动在线时可拨动写入，底层为 `POST /api/io/write`）
- 串口调试页：`http://127.0.0.1:5080/debugSerialDev.html`（端口配置、文本/十六进制收发、实时状态监控）
- 平台步进示教：`http://127.0.0.1:5080/monitorPlatform.html?deviceId={platformId}`（步进点动、使能、示教点；平台设备也可从综合监控页设备表跳转）
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
- `ProjectName`
- `IsRunning`
- `Drivers`
- `Devices`（`gpio` / `vio` 含 `gpioIoPoints`；`platform` 另含 `platformAxes`；`serialdev` 含 `serialPortInfo`）
- `Vars`

该快照来自 `MdkRuntime.GetSnapshot()`，可直接用于后续扩展 API / WebSocket / 历史存储。

---

## 7. 本地运行

**环境**：Windows x64；CEF 模式需安装 [Visual C++ 2019+ 可再发行组件](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)。主工程目标为 `win-x64`，输出在 `src/bin/{Configuration}/net8.0-windows10.0.22621.0/win-x64/`。

在仓库根目录构建与测试：

```bash
dotnet build MDKOSS.sln -c Release
dotnet test MDKOSS.sln -c Release --no-build
```

WinForms 监控（默认）：

```bash
dotnet run --project src/MDKOSS.csproj
```

CEF 桌面壳（`views/index.html`）：

```bash
dotnet run --project src/MDKOSS.csproj -- --cef
```

也可使用根目录脚本 `run-src-mdkoss.bat` / `run-src-mdkoss-cef.bat`。

控制台模式（无 GUI，使用默认配置文件）：

```bash
dotnet run --project src/MDKOSS.csproj -- --console
```

默认配置路径为可执行文件目录下的 `configs/sample.setting.json`。日志目录为输出目录下的 `logs/yyyyMMdd.log`。

控制台模式看到如下输出表示启动成功：

- `MDKOSS runtime started.`
- `Monitor UI: http://127.0.0.1:5080/`（或 `http://localhost:5080/`）

CEF / WinForms 模式请查看 `logs/` 与窗体；浏览器亦可直接访问上述 Monitor URL。

---

## 8. 下一步建议

近期已补齐：HMI 主界面 `index.html`、`core/monitor/` 监控服务模块、**CefSharp.NETCore** 桌面壳（`--cef`）、`Program.cs` 统一运行时启停与 **NLog** 日志（`AppLog`，Debug 下每次启动清空当日日志）、WinForms 监控、`DriverFactory` / `RuntimeTaskFactory`、GPIO / 平台 / VIO 参数解析、`PlatformDevice` / `VioDevice`、`tests/MDKOSS.Tests`（xUnit）与 GitHub Actions 构建测试。

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

