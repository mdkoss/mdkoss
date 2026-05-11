# MDKOSS（Open Source Simplified Runtime）

`MDKOSS` 是从 `MDKSYS/mdkruntime` 提炼出的开源简化运行时。当前版本已形成可运行闭环：

- 配置加载（JSON -> Runtime）
- 驱动抽象与实例化（`IDriver` + `DrvGts` / `DrvSim`）
- 设备组件体系（`gpio` / `vio` / `axis` / `platform`（XY…XYZUVW 多轴）/ `cameradev`）
- 任务调度与心跳更新（`MTaskScheduler`）
- 变量中心（`MVarStore`）
- 基础监控界面（`HttpListener + HTML Dashboard`）

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
- 可通过网页查看基础状态面板

### 1.2 暂不纳入
- 完整 Nancy API 模块体系
- CefSharp 嵌入式桌面 UI
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
    │   └── monitoringpage.html
    ├── tasks/
    ├── gui/
    │   └── winform/
    └── core/
        ├── mdk.cs
        ├── msetting.cs
        ├── mdev.cs
        ├── mtask.cs
        ├── mvar.cs
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
        └── monitoring/
            ├── monitoringserver.cs
            └── monitoringpage.cs
```

构建后，`configs` 与 `views` 会复制到输出目录（与可执行文件同级），运行时默认从 `configs/sample.setting.json` 加载配置。

---

## 3. 模块职责

- `Program.cs`  
  应用入口（WinForms 或 `--console`）。控制台模式默认从 `MdkSetting.DefaultSettingsPath`（即输出目录下的 `configs/sample.setting.json`）加载配置，启动 `MdkRuntime` 与监控服务，并处理退出流程。

- `src/core/mdk.cs`  
  Runtime Host。统一管理生命周期：`Initialize -> Start -> StopAsync -> Dispose`。内部完成变量、驱动、设备、任务的注册与编排。

- `src/core/msetting.cs`  
  配置模型与加载器。定义 `DriverConfig`、`DeviceConfig`、`TaskConfig`，支持从 JSON 反序列化；`DefaultSettingsPath` 指向与程序同目录的 `configs/sample.setting.json`。

- `src/core/mdev.cs`  
  设备体系。包含设备基类 `MDeviceBase` 及 `GpioDevice` / `VioDevice` / `AxisDevice` / `PlatformDevice` / `CameraDevDevice` 子类。`PlatformDevice` 由多条 `AxisDevice` 组成，轴布局由 `MPlatformKind`（XY、XYZ、XYZU、XYZUV、XYZUVW）描述。

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

- `src/core/monitoring/monitoringserver.cs`  
  轻量监控服务，提供：
  - `GET /`：监控页面
  - `GET /api/status`：运行时快照 JSON

- `src/core/monitoring/monitoringpage.cs`  
  前端监控页面，展示项目状态、驱动状态、变量快照（轮询更新）。

---

## 4. 运行时架构与数据流

### 4.1 分层架构

1. **Configuration Layer**：`MdkSetting`
2. **Runtime Orchestration Layer**：`MdkRuntime`
3. **Driver Layer**：`IDriver` / `DrvGts` / `DrvSim`
4. **Device Layer**：`MDeviceBase` + 设备子类
5. **Task Layer**：`MTaskScheduler` + `MTaskBase`
6. **State Layer**：`MVarStore`
7. **Monitoring Layer**：`MonitoringServer` + `MonitoringPage`

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
  - `axis` / `cameradev`：单驱动设备，依赖 `driverId`。
- `tasks[]`：
  - `name` / `driverId` / `intervalMs` / `parameters`
- `vars`：初始变量字典

支持设备类型（`type` 字段，大小写不敏感）：  
- `gpio`：多驱动物理 IO 路由  
- `vio`：单驱动虚拟 IO（见上）  
- `axis`  
- `platform`：另支持 `xy` / `xyz` / `xyzu` / `xyzuv` / `xyzuvw` 作为与 `kind` 等价的 `type` 简写  
- `cameradev`  

`configs/sample.setting.json` 中已包含上述类型的示例条目，便于本地对照与联调。

---

## 6. 监控能力

运行后可访问：

- 页面：`http://127.0.0.1:5080/`（或 `http://localhost:5080/`）
- 接口：`http://127.0.0.1:5080/api/status`

`/api/status` 返回：
- `ProjectName`
- `IsRunning`
- `Drivers`
- `Devices`（`gpio` / `vio` 含 `gpioIoPoints`；`platform` 另含 `platformAxes`）
- `Vars`

该快照来自 `MdkRuntime.GetSnapshot()`，可直接用于后续扩展 API / WebSocket / 历史存储。

---

## 7. 本地运行

在仓库根目录执行：

```bash
dotnet run --project src/MDKOSS.csproj
```

控制台模式（无 GUI，使用默认配置文件）：

```bash
dotnet run --project src/MDKOSS.csproj -- --console
```

运行单元测试（需 Windows 目标框架，与主工程一致）：

```bash
dotnet test MDKOSS.sln -c Release
```

默认配置路径为可执行文件目录下的 `configs/sample.setting.json`。

看到如下输出表示启动成功：

- `MDKOSS runtime started.`
- `Monitor UI: http://127.0.0.1:5080/`（或 `http://localhost:5080/`）

---

## 8. 下一步建议

近期已补齐：监控页 `Devices` 表格、`DriverFactory` / `RuntimeTaskFactory`、GPIO / 平台 / VIO 参数解析模块、`MdkErrorCode` / `MdkException`（含 `PlatformConfigurationInvalid`、`VioBindingInvalid` 等）、多轴 `PlatformDevice`、`VioDevice`、`tests/MDKOSS.Tests`（xUnit）、`MDKOSS.sln` 与 GitHub Actions 构建与测试。运行时已集成 **NLog** 文件日志（`AppLog`）。

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

