# MDKOSS（Open Source Simplified Runtime）

`MDKOSS` 是从 `MDKSYS/mdkruntime` 提炼出的开源简化运行时。当前版本已形成可运行闭环：

- 配置加载（JSON -> Runtime）
- 驱动抽象与实例化（`IDriver` + `DrvGts`）
- 设备组件体系（`gpio / axis / platform / cameradev`）
- 任务调度与心跳更新（`MTaskScheduler`）
- 变量中心（`MVarStore`）
- 基础监控界面（`HttpListener + HTML Dashboard`）

---

## 1. 当前设计目标

以最小依赖实现“可编译、可运行、可观测、可扩展”的运行时内核，保留 `mdkruntime` 的核心思想，去掉第一阶段非必需复杂度（桌面容器、复杂服务编排、Redis 同步等）。

### 1.1 已实现目标
- 可从 `sample.setting.json` 创建运行时实例
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
MDKOSS/
├── MDKOSS.csproj
├── Program.cs
├── sample.setting.json
├── readme.md
└── src/
    └── core/
        ├── mdk.cs
        ├── msetting.cs
        ├── mdev.cs
        ├── mtask.cs
        ├── mvar.cs
        ├── drivers/
        │   ├── idriver.cs
        │   └── drvgts.cs
        └── monitoring/
            ├── monitoringserver.cs
            └── monitoringpage.cs
```

---

## 3. 模块职责

- `Program.cs`  
  控制台入口。负责加载配置、启动 `MdkRuntime`、启动监控服务，并处理退出流程。

- `src/core/mdk.cs`  
  Runtime Host。统一管理生命周期：`Initialize -> Start -> StopAsync -> Dispose`。内部完成变量、驱动、设备、任务的注册与编排。

- `src/core/msetting.cs`  
  配置模型与加载器。定义 `DriverConfig`、`DeviceConfig`、`TaskConfig`，并支持从 JSON 反序列化。

- `src/core/mdev.cs`  
  设备体系。包含设备基类 `MDeviceBase` 及 `GpioDevice` / `AxisDevice` / `PlatformDevice` / `CameraDevDevice` 子类。

- `src/core/mtask.cs`  
  任务体系。包含 `MTaskBase` 和 `MTaskScheduler`，并提供基础任务 `PollDriverTask` 用于驱动心跳监控。

- `src/core/mvar.cs`  
  线程安全变量中心。提供 `Set/Get/TryGet/Snapshot`，用于模块间状态共享与监控导出。

- `src/core/drivers/idriver.cs`  
  驱动统一接口，约束驱动初始化、连接状态、读写行为。

- `src/core/drivers/drvgts.cs`  
  GTS 示例驱动（内存映射模拟），用于本地联调和端到端流程验证。

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
3. **Driver Layer**：`IDriver` / `DrvGts`
4. **Device Layer**：`MDeviceBase` + 设备子类
5. **Task Layer**：`MTaskScheduler` + `MTaskBase`
6. **State Layer**：`MVarStore`
7. **Monitoring Layer**：`MonitoringServer` + `MonitoringPage`

### 4.2 启动顺序

1. 读取配置（`MdkSetting.Load`）
2. `MdkRuntime.Initialize()`：
   - Seed Vars
   - 初始化 Drivers
   - 初始化 Devices
   - 注册 Tasks
3. `MdkRuntime.Start()`：
   - 启动 Devices
   - 启动 Scheduler
4. 启动监控服务（默认 `http://localhost:5080/`）

### 4.3 停止顺序

1. `MdkRuntime.StopAsync()`：
   - 先停 Task Scheduler
   - 再停 Devices
2. `Dispose()`：
   - 释放 Drivers / Devices / Scheduler

---

## 5. 配置模型（当前版本）

`sample.setting.json` 包含如下核心字段：

- `projectName`：项目名称
- `cycleMs`：主循环周期（预留）
- `drivers[]`：
  - `id` / `type` / `enabled` / `parameters`
- `devices[]`：
  - `id` / `name` / `type` / `driverId` / `enabled` / `parameters`
- `tasks[]`：
  - `name` / `driverId` / `intervalMs` / `parameters`
- `vars`：初始变量字典

支持设备类型：
- `gpio`
- `axis`
- `platform`
- `cameradev`

---

## 6. 监控能力

运行后可访问：

- 页面：`http://localhost:5080/`
- 接口：`http://localhost:5080/api/status`

`/api/status` 返回：
- `ProjectName`
- `IsRunning`
- `Drivers`
- `Devices`
- `Vars`

该快照来自 `MdkRuntime.GetSnapshot()`，可直接用于后续扩展 API / WebSocket / 历史存储。

---

## 7. 本地运行

在仓库根目录执行：

```bash
dotnet run --project MDKOSS/MDKOSS.csproj
```

看到如下输出表示启动成功：

- `MDKOSS runtime started.`
- `Monitor UI: http://localhost:5080/`

---

## 8. 下一步建议

- 在监控页面增加 `Devices` 表格与设备状态详情
- 增加 `IDriver` 实现注册工厂（替代 `switch`）
- 为 `DeviceConfig` 增加更明确的参数模型
- 增加单元测试（Setting 解析、Scheduler 生命周期、Snapshot 一致性）
- 增加日志组件与错误码规范

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

---

## 11. Star History

项目 Star 趋势（Star History）：

[![Star History Chart](https://starchart.cc/mdkoss/mdkoss.svg?variant=adaptive)](https://starchart.cc/mdkoss/mdkoss)

Star trend chart for this project:

[![Star History Chart](https://starchart.cc/mdkoss/mdkoss.svg?variant=adaptive)](https://starchart.cc/mdkoss/mdkoss)
