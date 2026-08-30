# MDKOSS.Tools.Calib

独立 WPF 标定工具。从任意机型的 `*.setting.json` 加载标定项目：每个项目是一个任务。
流程型用 `type=flow`（可编辑），代码型继承 `MotionTask` 并只在本工具注册 `calib.*` 类型。

参数与结果写入 Runtime 的 SQLite，换机、重开工具后仍可回看最近一次标定。

## 界面

| 区域 | 内容 |
|------|------|
| 左 | 标定项目列表（配置中的 calib 任务） |
| 左中 | 标定参数（写入 `task.{name}.param.*`、TaskConfig、`calib_params`） |
| 右上 | 执行结果（`task.{name}.calib.*`，结束时写入 `calib_results`） |
| 右下 | 执行过程（phase / flow.pc / op.log） |

菜单：打开配置、保存配置、应用参数、运行、停止、编辑流程（仅 Flow）。

## 配置约定

任务被识别为标定项，当：

- `parameters.calib` 为 `true`，或
- `type` 以 `calib` 开头

### FlowTask（机型宿主可用）

```json
"type": "flow",
"parameters": {
  "calib": "true",
  "displayName": "贴装头 XY 偏置",
  "group": "标定",
  "loop": "false",
  "autoStart": "false",
  "flowFile": "configs/flows/xxx.flow.json",
  "expectedX": "10"
}
```

- `flowFile` 先按 setting JSON 所在目录解析，再回退进程目录；也可用内联 `flowJson`。
- 「编辑流程」写回文件或 `flowJson`。
- 机型宿主（DieBonder / Dispenser / Pnp / Sample / Sample.Tools）**只增加 `type=flow`**，避免未注册的 `calib.*` 导致启动失败。
- `autoStart=false`、`loop=false`：宿主启动时不自动跑标定。

流程约定：用 `motion.setTaskVar` 写 `calib.ok` / `calib.offset*` / `calib.message`，工具结果区按 `task.{name}.calib.*` 显示。

### MotionTask（仅本工具）

继承 `CalibMotionTaskBase`，在 `CalibExtension` 里 `registration.Task("calib.xxx", ...)`。
UI 下发 `task.{name}.command` = `start` / `stop` / `reset`。

| type | 说明 | 主要参数 |
|------|------|----------|
| `calib.axisoffset` | 单轴使能 → 定位 → 编码器减目标 | `axisDeviceId` `expectedPos` `settleTicks` |
| `calib.platformoffset` | 平台单轴使能 → 定位 → 偏置 | `platformDeviceId` `axisLetter` `expectedPos` |
| `calib.ninepoint` | 平台 3×3 九点，均值偏置与残差 | `platformDeviceId` `originX/Y` `pitch` `maxResidual` |

不要把上述 type 写进 DieBonder / Dispenser / Pnp / Sample 的 setting。

## 各示例标定流程

用本工具「打开」对应 `sample.setting.json` 即可执行。流程文件在各项目 `configs/flows/`。

| 宿主 | 项目 | 任务 | 对象 |
|------|------|------|------|
| DieBonder | 贴装头 XY 偏置 | `calib-head-xy` | `head-bond` X→10 |
| DieBonder | 贴装头 Z 高度 | `calib-head-z` | `head-bond` Z→-2 |
| DieBonder | 上视 U 角补偿 | `calib-head-u` | `head-bond` U→5 |
| Dispenser | 点胶头 XY 偏置 | `calib-head-xy` | `head-dispense` X→10 |
| Dispenser | 点胶 Z 高度 | `calib-z-height` | `head-dispense` Z→-6 |
| Pnp | 机器人 XY 偏置 | `calib-robot-xy` | `robot-xyz` X→10 |
| Pnp | 取料 Z 高度 | `calib-pick-z` | `robot-xyz` Z→-12 |
| Sample | 演示头 XY 偏置 | `calib-head-demo` | `head-demo` X→5 |
| Sample.Tools | XY 平台偏置 | `calib-platform-xy` | `platform-xy` X→10 |
| Sample.Tools | XYZ 平台 Z | `calib-platform-z` | `platform-xyz` Z→-4 |
| Tools.Calib | 轴 / 九点 / 平台偏置 / 平台 Flow | 见自带 `configs/sample.setting.json` | 仿真轴与 `platform-xy` |

Modbus / Iec61131 无运动栈，不配标定项。

## 数据库

表在 schema v5，随 Runtime 的 `databasePath`（默认 `data/mdk.db`）创建。

| 表 | 作用 |
|----|------|
| `calib_params` | 最新参数，主键 `(project_name, task_name)` |
| `calib_results` | 运行历史：当时参数、`results_json`、`ok`、`message`、`created_at` |

- 应用参数 → 覆盖 `calib_params`
- 运行结束 → 追加 `calib_results`
- 再次打开同一 `projectName` + 任务 → 参数网格用库中最新值，结果区无实时变量时显示最近一次结果

API：`MdkDataStore.TryUpsertCalibParams` / `TryGetCalibParams` / `TryInsertCalibResult` / `TryGetLatestCalibResult`。工具侧封装在 `CalibStore`。

## 运行

```bash
dotnet run --project src/MDKOSS.Tools.Calib/MDKOSS.Tools.Calib.csproj -c Debug
```

或 `--setting` 指定机型配置，例如：

```bash
dotnet run --project src/MDKOSS.Tools.Calib/MDKOSS.Tools.Calib.csproj -- --setting src/MDKOSS.Sample.DieBonder/configs/sample.setting.json
```

默认读 exe 旁 `configs/` 下第一个 JSON。VS Code 启动配置：`MDKOSS.Tools.Calib`。

## 测试

```bash
dotnet test tests/MDKOSS.Tests/MDKOSS.Tests.csproj -c Debug --filter FullyQualifiedName~Calib
```

覆盖：目录识别、各示例 setting + flow 校验、`flowFile` 相对 setting 解析、MotionTask 运行、参数/结果入库。
