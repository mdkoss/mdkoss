# MDKOSS.Sample — 半导体贴片机（Die Bonder）

本 Sample 以 **半导体贴片机 / Die Bonder** 为默认机型：配置驱动/设备/任务，在 Sample 内实现贴装任务与定制 HMI；Tray 设备复用 `MDKOSS.Pnp` 插件。

## 1. 机型组件

```text
                    ┌───────────── drv-motion ─────────────┐
                    │  head-bond (XYZU)                     │
                    │  cam-downlook / cam-uplook            │
                    └──────────────────────────────────────┘
                                      │
  tray-wafer ──► 取料 ──► 上视测角 ──► 贴装 ──► tray-substrate
  (晶圆源盘)                              │         (基板 nest)
                                      │
                    ┌───────────── drv-io ─────────────────┐
                    │  gpio-machine：真空/顶针/传送带/塔灯   │
                    └──────────────────────────────────────┘
```

| 组件 ID | 类型 | 职责 |
|---------|------|------|
| `drv-motion` | sim | 运动控制卡：贴装头轴 + 相机 |
| `drv-io` | sim | IO 卡：真空、顶针、传送带、塔灯、安全 |
| `gpio-machine` | gpio | 整机数字量映射 |
| `head-bond` | xyzu | 贴装头 Bond Head（X/Y/Z/U） |
| `cam-downlook` | cameradev | 下视：晶圆 Die / 焊盘 XY 定位 |
| `cam-uplook` | cameradev | 上视：Die 角度 → U 轴补偿 |
| `tray-wafer` | tray | 源料盘（Waffle / Gel-Pak，8×8） |
| `tray-substrate` | tray | 目标基板（Leadframe / PCB，2×8） |

## 2. 工艺与任务

```text
晶圆盘到位 → 安全检查 → 下视定位 Die
    → 贴装头移至拾取位 → 顶针 → Z 下降 → 吸真空 → 上升
    → 移至上视工位测角 → 计算基板 nest + U 补偿
    →（可选点胶）→ 移至基板 → 下降放料 → 上升
    → nest 前进；盘用尽时请求换盘
```

| 任务 | type | 实现 | 职责 |
|------|------|------|------|
| `task-operation` | operation | Core | 启停 / 塔灯 |
| `task-cycle` | cycle | Core | 运行态聚合 |
| `poll-motion` / `poll-io` | pollDriver | Core | 驱动心跳 |
| `bond-cycle` | **bond** | Sample `BondCycleTask` | 贴装主循环 |
| `material-conveyor` | **materialConveyor** | Sample `MaterialConveyorTask` | 响应换盘 |

`bond` 增强（相对通用 PNP）：

- 安全门检测（`checkSafetyDoor`；仿真默认关）
- 拾取顶针（`useEjector`）
- 可选点胶（`useDispenser`）

## 3. Sample 定制界面与 API（对齐 Cef 页面分组）

输出目录 `views/` 由引用叠加（**不要**再对 Cef/Pnp 的 views 做 `Link`，否则易出现 `views\views` 嵌套）：

| 来源 | 方式 | 内容 |
|------|------|------|
| `MDKOSS.Cef` | `ProjectReference` → Content | 核心：`index` / `popup_*` / `monitor_*` / `debug_*` / `man_*` / `css` / `js` |
| `MDKOSS.Sample/views/**` | 本项目 `None` | 机型：`indexDieBonder.html` / `monitorDieBonder.html` |
| `MDKOSS.Pnp` | `ProjectReference` → None | 可选 PNP 演示页 |

```text
src/MDKOSS.Sample/
├── DieBonder/
│   ├── DieBonderExtension.cs   # 注册 task / API / 静态页
│   ├── BondCycleTask.cs
│   ├── MaterialConveyorTask.cs
│   ├── BondLogStore.cs
│   ├── DieBonderApiModule.cs   # /api/bond/*
│   └── DieBonderViewPages.cs
└── views/
    ├── indexDieBonder.html     # 机型主界面
    └── monitorDieBonder.html   # 循环细节
```

| 能力 | 路径 |
|------|------|
| 机型主界面 | `/indexDieBonder.html`（配置 `startPage`） |
| 循环监控 | `/monitorDieBonder.html` |
| 系统主界面 | `/index.html`（Cef） |
| IO / 平台监控 | `/monitor_io.html` · `/monitor_platform.html` |
| 平台示教 | `/debug_platform.html?deviceId=head-bond` |
| 运行总览 | `/monitor_runtime.html` |
| Dashboard | `GET /api/bond/dashboard` |
| 日志 | `GET /api/bond/logs` |
| 启停复位换盘 | `POST /api/bond/start\|stop\|reset\|traychange\|clearlogs` |

`Program.Main` 在插件发现后 `Register(new DieBonderExtension())`。  
Tray 仍由 `MDKOSS.Pnp` 提供；协调变量继续写 `task.pnp.*`，同时发布 `task.bond.*`。

## 4. 配方

| ID | 产品 | 要点 |
|----|------|------|
| `qfn-3x3` | QFN 3×3mm | 默认；小角度、低噪声 |
| `bga-10x10` | BGA 10×10mm | 更大角度窗口与放置偏置 |
| `flipchip` | Flip-Chip | U 预偏置 180°，严格角度 |

## 5. 配置与运行

| 文件 | 说明 |
|------|------|
| `configs/sample.setting.json` | **默认**：半导体贴片机 |
| `configs/pnp.setting.json` | 通用 PNP（`examples/pnp` 拷贝） |

```bash
# CEF → startPage（sample.setting.json → indexDieBonder.html）
dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj

# 控制台
dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj -- --console

# 通用 PNP 配置（startPage → indexPnp.html）
dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj -- --setting configs/pnp.setting.json
```

监控入口：

- `http://127.0.0.1:5080/indexDieBonder.html` — 机型主界面
- `http://127.0.0.1:5080/monitorDieBonder.html` — 循环细节
- `http://127.0.0.1:5080/index.html` — 系统主界面（Cef）
- `http://127.0.0.1:5080/monitor_runtime.html` — 运行总览
- `http://127.0.0.1:5080/api/bond/dashboard`

不同工程用 `projectName` 区分身份，用 `startPage` 指定启动页；`RuntimeHost` 不再按机型硬编码判断。
