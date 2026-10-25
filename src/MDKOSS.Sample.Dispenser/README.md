# MDKOSS.Sample.Dispenser — 三轴点胶机

给现场工程师或合作方用的**仿真点胶样例**：不接实物也能在本机走完一个点阵列。  
需要 **.NET 8 桌面运行时** 与 **Microsoft Visual C++ 2019 x64 运行库**（CEF 宿主依赖）。Windows x64。

## 10 分钟跑通

1. 在仓库根目录执行：

```bat
run-dispenser.bat -- --console
```

或：

```bash
dotnet run --project src/MDKOSS.Sample.Dispenser/MDKOSS.Sample.Dispenser.csproj -c Debug -r win-x64 -- --console
```

不加 `--console` 时走 CEF 桌面窗体；有浏览器时用控制台模式即可。

2. 浏览器打开：`http://127.0.0.1:5082/indexDispenser.html`

3. 点页面上的 **启动**（`POST /api/dispense/start`）。默认配方 `grid-2x2`（四点）。任务参数 `checkWorkpieceIo` 为 `false`，仿真不接工件传感器也能进入循环。

4. 看主界面阶段文字，或轮询 `GET /api/dispense/dashboard` 的 `phase` 字段。四点走完后为 `Done`。循环状态以 `task.dispense.*` 为准（`phase` / `message` / `pointIndex` / `valve`），不要用 `task.operation.state` 判断结束。

5. **停止** → `POST /api/dispense/stop`；**复位** → `POST /api/dispense/reset`（清计数与日志，阶段回 Idle）。

默认加载 `configs/` 下按文件名排序的第一个 `*.setting.json`（当前是仿真 `sample.setting.json`；可选真卡见下方「真卡配置」）。默认运动与 IO 均为 `type: sim`。

一键冒烟（冷机到 `Done`、断言阀关；本机 CI 外再跑）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\smoke-dispenser.ps1
```

### 常见失败

| 现象 | 处理 |
|------|------|
| 端口 5082 占用 | 改 `monitoringPrefix` 或结束占用进程 |
| 缺 .NET 8 / VC++ | 安装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0) 与 [VC++ 2015–2022 x64](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)（不必装 IDE） |
| 页面「连接失败」 | 确认 `--console` 已起、防火墙未拦回环 |
| 卡在 WaitWorkpiece | 确认加载的是 `sample.setting.json` 且 `checkWorkpieceIo=false` |

---

## 1. 机型组件

```text
                    ┌───────────── drv-motion ─────────────┐
                    │  head-dispense (XYZ)                  │
                    │  axis-x / axis-y / axis-z             │
                    └──────────────────────────────────────┘
                                      │
                    ┌───────────── drv-io ─────────────────┐
                    │  gpio-machine：阀 / 塔灯 / 工件 / 安全  │
                    └──────────────────────────────────────┘
```

| 组件 ID | 类型 | 职责 |
|---------|------|------|
| `drv-motion` | sim | 运动控制卡：点胶头 XYZ |
| `drv-io` | sim | IO 卡：点胶阀、塔灯、安全、工件到位 |
| `gpio-machine` | gpio | 整机数字量映射 |
| `axis-x` / `axis-y` / `axis-z` | linear | 三直线轴 |
| `head-dispense` | xyz | 点胶头平台 |

### `gpio-machine` 别名（仿真）

摘自 `configs/sample.setting.json`。地址仍是仿真写法，不是雷赛点号。

| 别名 | 方向 | 仿真地址 | 作用 |
|------|------|----------|------|
| `startButton` | DI | `drv-io\|di.gpi.bit.0` | 启动 |
| `stopButton` | DI | `drv-io\|di.gpi.bit.1` | 停止 |
| `resetButton` | DI | `drv-io\|di.gpi.bit.2` | 复位 |
| `safetyDoor` | DI | `drv-io\|di.gpi.bit.3` | 安全门 |
| `workpiece.present` | DI | `drv-io\|di.gpi.bit.10` | 工件到位 |
| `tower.red` | DO | `drv-io\|do.gpo.bit.0` | 塔灯红 |
| `tower.yellow` | DO | `drv-io\|do.gpo.bit.1` | 塔灯黄 |
| `tower.green` | DO | `drv-io\|do.gpo.bit.2` | 塔灯绿 |
| `buzzer` | DO | `drv-io\|do.gpo.bit.3` | 蜂鸣器 |
| `valve` | DO | `drv-io\|do.gpo.bit.10` | 点胶阀 |

## 2. 工艺与任务

```text
等工件 → 使能 → 抬到安全高度 → 走 XY
    → Z 下降 → 开阀 → 停留 → 关阀 → 抬起 → 下一点
    → 点阵走完后 Done
```

| 任务 | type | 实现 | 职责 |
|------|------|------|------|
| `task-operation` | operation | Core | 启停 / 塔灯 |
| `task-cycle` | cycle | Core | 运行态聚合 |
| `poll-motion` / `poll-io` | pollDriver | Core | 驱动心跳 |
| `dispense-cycle` | **dispense** | `DispenseCycleTask` | 点胶主循环 |

点阵由配方 `dispense.rows` × `dispense.cols` 与原点/间距生成。

## 3. 界面与 API

| 能力 | 路径 |
|------|------|
| 机型主界面 | `/indexDispenser.html`（配置 `startPage`） |
| 系统主界面 | `/index.html`（Cef） |
| 点胶头监控 / 示教 | `/monitor_platform.html?deviceId=head-dispense` · `/debug_platform.html?deviceId=head-dispense` |
| Dashboard | `GET /api/dispense/dashboard` |
| 启停复位 | `POST /api/dispense/start\|stop\|reset\|clearlogs` |

页面按钮与 API 一一对应：启动→`start`，停止→`stop`，复位→`reset`。  
`Program.Main` 在插件发现后 `Register(new DispenserExtension())`。

## 4. 配方

| ID | 说明 |
|----|------|
| `grid-2x2` | 默认四点球阵，短开阀 |
| `grid-3x3` | 九点阵列，开阀稍长 |

## 5. 运行（补充）

仓库根目录：

```bat
run-dispenser.bat
run-dispenser.bat -- --console
```

监控入口：`http://127.0.0.1:5082/indexDispenser.html`

## 6. 标定

`type=flow` 且 `calib=true`、`autoStart=false`。用 [MDKOSS.Tools.Calib](../MDKOSS.Tools.Calib/README.md) 打开本配置执行；参数与结果写入 SQLite。

| 任务 | 流程 | 对象 |
|------|------|------|
| `calib-head-xy` | `configs/flows/calib-head-xy.flow.json` | `head-dispense` X |
| `calib-z-height` | `configs/flows/calib-z-height.flow.json` | `head-dispense` Z |

## 7. 真卡配置

**真卡未在本版本验证。** 没有雷赛卡与 `LTDMC.dll` 时不要加载本节文件；对外演示请用仿真 `sample.setting.json`。

可选文件：`configs/x-dmc.setting.json`（文件名排在 `sample.setting.json` 之后，**不会**成为 `dotnet run` 默认）。内容约定：

| 项 | 取值 |
|----|------|
| `drv-motion` | `type: dmc`，参数仅用 `card` / `crd` / `configPath` / `resetOnInit` / `sevonActiveLow` |
| `drv-io` | 仍为 `sim`（运动真卡、IO 仍仿真，未整机联调） |
| `checkWorkpieceIo` | `true`；`task.dispense.workpiecePresent` 默认 `false` |
| 自动循环 | `task.dispense.run` 为 `false`，上电不自动 `start` |
| 急停 | `gpio-machine` 仅有 `safetyDoor`，**没有急停输入** → 本文件只允许点动，不称为可产线循环 |

加载方式（不改 Core 选文件规则）：真机试验时临时移走或改名 `sample.setting.json`，使 `x-dmc.setting.json` 成为 `configs/` 下第一个 `*.setting.json`；或显式传 `--setting configs/x-dmc.setting.json`。

操作顺序：先打开 `/debug_platform.html?deviceId=head-dispense` 点动，确认方向与限位，再谈循环。Config.Wpf 可直接打开该 JSON（驱动类型 `dmc` 已注册）。

## 8. 点胶包里有什么文件

| 路径 | 作用 |
|------|------|
| `run-dispenser.bat`（仓库根） | 一键启动本样例 |
| `smoke-dispenser.ps1`（仓库根） | 冷机冒烟到 Done（阀关） |
| `configs/sample.setting.json` | 默认仿真配置 |
| `configs/x-dmc.setting.json` | 可选雷赛运动配置（未实机验证） |
| `configs/hmi.layout.json` | HMI 组态 |
| `views/indexDispenser.html` | 机型主界面 |
| `Dispenser/*` | 点胶任务 / API / 扩展注册 |
| `README.md` | 本说明 |
