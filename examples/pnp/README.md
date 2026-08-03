# PNP 机型说明（Pick and Place 示例）

本目录是基于 MDKOSS 框架的 **拾取放置（PNP）机型示例**：用配置描述设备与任务，用 `MotionTask` 状态机实现工艺，用监控页与 `/api/pnp` 做操作界面。

## 1. 工艺流程

```text
源传送带 Tray 到位
        │
        ▼
  顶部相机识别产品 XY
        │
        ▼
  机械手移动到拾取位 → 下降 → 吸真空 → 上升
        │
        ▼
  移动到底部相机工位 → 测量产品角度
        │
        ▼
  计算放置位姿（目标 Tray nest + 角度补偿 U）
        │
        ▼
  移动到目标 Tray → 下降 → 放料 → 上升
        │
        ▼
  源/目标 nest 前进；Tray 用尽时请求换盘
```

| 环节 | 实现 |
|------|------|
| Tray 盘 | 扩展设备 `tray`（行列 nest、节距、原点） |
| 源/目标传送带 | `pnpConveyor` 任务 + GPIO 传送带输出 |
| 机械手 | `platform` / `xyzu`（X/Y/Z/U） |
| 顶部相机 | `cameradev` + 周期任务内 `TriggerCapture`（示例为仿真结果） |
| 底部相机 | 同上，输出角度并参与放置 U 轴补偿 |
| 真空 | GPIO `out.vacuum` |

## 2. 目录结构

```text
examples/pnp/
├── MDKOSS.Pnp.csproj
├── PnpBootstrap.cs           # 设备 / 任务 / API / 静态页注册
├── PnpLogStore.cs            # 执行日志环形缓冲
├── README.md                 # 本说明
├── configs/
│   └── pnp.setting.json      # 机型工程配置
├── devices/
│   ├── traydev.cs
│   └── tray_device_parameters.cs
├── tasks/
│   ├── task_pnp_cycle.cs     # 主循环状态机（: MotionTask）
│   └── task_pnp_conveyor.cs  # 换盘 / 传送带
├── server/
│   ├── api_pnp_module.cs     # /api/pnp/*
│   └── monitorpnppage.cs
└── views/
    ├── indexPnp.html         # 机型主界面（模块状态 + 日志）
    └── monitorPnp.html       # 循环细节页
```

## 3. 任务组合

机型不是单一巨型任务，而是 **并行任务 + 共享变量** 协作（与框架设计一致）：

| 配置 type | 任务 | 职责 |
|-----------|------|------|
| `operation` | task-operation | 启停/塔灯（`task.operation.command`） |
| `cycle` | task-cycle | 运行态聚合 |
| `pollDriver` | poll-main | 驱动心跳 |
| `pnp` | pnp-cycle | 拾取-视觉-放置状态机 |
| `pnpConveyor` | pnp-conveyor | 响应 `task.pnp.trayChangeRequest` 换盘 |

协调变量（节选）：

- `task.pnp.command` / `task.pnp.run` — 启停
- `task.pnp.phase` / `task.pnp.message` — 当前阶段
- `task.pnp.srcTrayPresent` / `task.pnp.tgtTrayPresent` — Tray 到位
- `task.pnp.trayChangeRequest` — 换盘请求
- `pnp.vision.top.*` / `pnp.vision.bottom.*` — 视觉结果
- 配方键 `pnp.bottomCam.*` / `pnp.place.*` / `pnp.vision.*`

## 4. 接入方式

在创建 `MdkRuntime` **之前**注册（CEF / Config 入口的 `Program.Main`）：

```csharp
MdkExtensionHost.DiscoverAndRegister(); // 自动加载 plugins 下驱动/设备/Pnp
```

使用 PNP 配置启动：

```bash
# 无 GUI 控制台（MDKOSS.Cef + --console）
dotnet run --project src/MDKOSS.Cef/MDKOSS.Cef.csproj -- --console --setting configs/pnp.setting.json

# CEF HMI（PNP 默认加载 indexPnp.html）
dotnet run --project src/MDKOSS.Cef/MDKOSS.Cef.csproj -- --setting configs/pnp.setting.json

# WinForms 配置 / 监控
dotnet run --project src/MDKOSS.Config/MDKOSS.Config.csproj -- --setting configs/pnp.setting.json
```

或根目录脚本：`run-pnp-cef.bat` / `run-pnp-console.bat`。

监控页：

- **机型主界面**：`http://127.0.0.1:5080/indexPnp.html`（模块状态 + 执行日志；PNP 配置默认 CEF 加载此页）
- 循环细节：`http://127.0.0.1:5080/monitorPnp.html`
- API：`GET /api/pnp/dashboard`（含 modules/logs），`GET /api/pnp/logs`，`POST /api/pnp/start|stop|reset|traychange|clearlogs`

## 5. 扩展点对照（框架）

| 能力 | 注册入口 |
|------|----------|
| Tray 设备类型 | `DeviceExtensionRegistry.Register("tray", …)` |
| Tray 动作 | `DeviceActionRegistry.Register` |
| 任务类型 `pnp` / `pnpConveyor` | `RuntimeTaskFactory.Register` |
| HTTP API | `MonitoringModuleRegistry.Register` |
| 静态页 | `StaticPageRegistry.Register("/monitorPnp.html", …)` |

视觉当前为 **可运行仿真**：触发 `CameraDevDevice.TriggerCapture` 后写入带噪声的 XY/角度。接入真实视觉时，把 `TryLocateTop` / `TryMeasureAngle` 中的仿真结果替换为相机/算法返回值即可，状态机与界面无需改动。

## 6. 配方切换

`pnp.setting.json` 内置 `chip-a` / `chip-b`。可通过监控配方 API 或 WinForms 配方工具切换；产品相关偏置与视觉配方名放在 `recipeVarKeys` 中，设备接线保留在 `devices[]`。