# CameraDev 调试界面说明

> 实现：`CameraDevDebugWindow.xaml` / `CameraDevDebugWindow.xaml.cs`

## 定位

调试相机类设备：**打开 / 关闭 / 采集**。列表包含：

| Type | 说明 |
|------|------|
| `cameradev` | Core 内置占位相机，依赖 `DriverId`，`TriggerCapture(recipe)` |
| `extcamera` | 扩展仿真相机（`MDKOSS.Extensions.Camera`），`Open` / `Close` / `capture` |

## 入口

- 菜单 **调试 → CameraDev 调试…**
- Devices 模块选中 `cameradev`/`extcamera` 后，右键 **打开调试界面…**

## 布局

| 区域 | 内容 |
|------|------|
| 顶栏 | 相机下拉、type 徽章、Open/Closed、打开/关闭/刷新 |
| 左 | Recipe、采集按钮、类型说明 |
| 右 | 状态文本（参数、vars、extcamera status JSON） |
| 底 | 日志 |

## 行为

### cameradev

1. 打开：`ConnectedDriver.Open(driverCfg)` → `new CameraDevDevice` → `Initialize` / `Start`
2. 采集：`CameraDevDevice.TriggerCapture(recipe)`（驱动 `Write` capture.*）
3. 关闭：`Stop` + Dispose

### extcamera

1. 打开：`DeviceExtensionRegistry.TryCreate("extcamera", …)` → `DeviceActionRegistry` action `open`
2. 采集：action `capture` / `trigger`，参数 `recipe`
3. 关闭：action `close` → `Stop` + Dispose
4. 刷新：action `status`

## 修改指引

1. **显示图像预览**：采集成功后若扩展返回图像路径/base64，在右侧加 `Image` 控件。
2. **仅支持某一后端**：按 `parameters.backend` 过滤下拉或禁用按钮。
3. **统一走 HTTP**：可改为调用本机 Monitoring `POST /api/devices/{id}/action`（需先起 `MdkRuntime`）。
4. **写回曝光等参数**：从 Status 区加编辑框，更新 `workspace.Setting` 对应设备 parameters。

## 依赖

- `App` 启动时插件发现（含 Camera 扩展）
- `cameradev` 需要 setting 中存在对应 Driver（常用 `sim`）
