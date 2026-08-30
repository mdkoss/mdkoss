# MDKOSS.Tools.Calib

独立 WPF 标定工具。从 `*.setting.json` 加载标定项目：每个项目是一个任务，
流程型用 `type=flow`（可编辑），代码型继承 `MotionTask` 并注册 `calib.*` 类型。

## 界面

| 区域 | 内容 |
|------|------|
| 左 | 标定项目列表（配置中的 calib 任务） |
| 左中 | 标定参数（写入 `task.{name}.param.*` 与 TaskConfig） |
| 右上 | 执行结果（`task.{name}.calib.*`） |
| 右下 | 执行过程（phase / flow.pc / op.log） |

## 配置约定

任务被识别为标定项，当：

- `parameters.calib` 为 `true`，或
- `type` 以 `calib` 开头

Flow 任务建议：

```json
"type": "flow",
"parameters": {
  "calib": "true",
  "loop": "false",
  "autoStart": "false",
  "flowFile": "configs/flows/xxx.flow.json"
}
```

`flowFile` 相对进程目录；也可用内联 `flowJson`。菜单「编辑流程」写回文件或 `flowJson`。

代码任务：继承 `CalibMotionTaskBase`（或 `MotionTask`），在 `CalibExtension` 里 `registration.Task("calib.xxx", ...)`。
UI 下发 `task.{name}.command` = `start` / `stop` / `reset`，读取 `task.{name}.calib.*`。

## 运行

```bash
dotnet run --project src/MDKOSS.Tools.Calib/MDKOSS.Tools.Calib.csproj -c Debug
```

或 `--setting` 指定配置。默认读 exe 旁 `configs/` 下第一个 JSON。自带仿真：轴偏置、九点、平台 Flow。
