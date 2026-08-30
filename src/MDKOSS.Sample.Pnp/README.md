# MDKOSS.Sample.Pnp — 拾取放置（Pick and Place）

本 Sample 以 **PNP** 为机型：配置驱动/设备/任务，在本项目内实现拾取-视觉-放置循环与定制 HMI。Tray 设备类型也供 `MDKOSS.Sample.DieBonder` 复用。

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
| 顶部 / 底部相机 | `cameradev` + 周期任务内 `TriggerCapture`（示例为仿真结果） |
| 真空 | GPIO `out.vacuum` |

## 2. 任务组合

| 配置 type | 任务 | 职责 |
|-----------|------|------|
| `operation` | task-operation | 启停/塔灯 |
| `cycle` | task-cycle | 运行态聚合 |
| `pollDriver` | poll-main | 驱动心跳 |
| `pnp` | pnp-cycle | 拾取-视觉-放置状态机 |
| `pnpConveyor` | pnp-conveyor | 响应 `task.pnp.trayChangeRequest` 换盘 |

`Program.Main` 在插件发现后 `Register(new PnpExtension())`。

## 3. 界面与 API

| 能力 | 路径 |
|------|------|
| 机型主界面 | `/indexPnp.html`（配置 `startPage`） |
| 循环监控 | `/monitorPnp.html` |
| Dashboard | `GET /api/pnp/dashboard` |
| 日志 | `GET /api/pnp/logs` |
| 启停复位换盘 | `POST /api/pnp/start\|stop\|reset\|traychange\|clearlogs` |

## 4. 运行

```bash
dotnet run --project src/MDKOSS.Sample.Pnp/MDKOSS.Sample.Pnp.csproj

dotnet run --project src/MDKOSS.Sample.Pnp/MDKOSS.Sample.Pnp.csproj -- --console
```

或根目录脚本：`run-pnp-cef.bat` / `run-pnp-console.bat`。

监控入口：`http://127.0.0.1:5084/indexPnp.html`

配方：`chip-a` / `chip-b`。视觉当前为可运行仿真；接入真机时替换 `TryLocateTop` / `TryMeasureAngle` 即可。

## 5. 标定

`type=flow` 且 `calib=true`、`autoStart=false`。用 [MDKOSS.Tools.Calib](../MDKOSS.Tools.Calib/README.md) 打开本配置执行；参数与结果写入 SQLite。

| 任务 | 流程 | 对象 |
|------|------|------|
| `calib-robot-xy` | `configs/flows/calib-robot-xy.flow.json` | `robot-xyz` X |
| `calib-pick-z` | `configs/flows/calib-pick-z.flow.json` | `robot-xyz` Z |
