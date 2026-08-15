# MDKOSS.Sample.Dispenser — 三轴点胶机

本 Sample 以 **三轴点胶机** 为机型：仿真 XYZ 点胶头 + 点胶阀 IO，在本项目内实现点胶循环任务与定制 HMI。

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

`Program.Main` 在插件发现后 `Register(new DispenserExtension())`。

## 4. 配方

| ID | 说明 |
|----|------|
| `grid-2x2` | 默认四点球阵，短开阀 |
| `grid-3x3` | 九点阵列，开阀稍长 |

## 5. 运行

```bash
dotnet run --project src/MDKOSS.Sample.Dispenser/MDKOSS.Sample.Dispenser.csproj

dotnet run --project src/MDKOSS.Sample.Dispenser/MDKOSS.Sample.Dispenser.csproj -- --console
```

监控入口：`http://127.0.0.1:5082/indexDispenser.html`
