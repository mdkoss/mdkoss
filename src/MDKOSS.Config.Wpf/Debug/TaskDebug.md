# Task 编辑界面说明

> 实现：`TaskDebugWindow.xaml` / `TaskDebugWindow.xaml.cs`  
> 入口：菜单 **调试 → Task 编辑…**，或 Tasks 模块右键 **打开调试界面…**

## 定位

针对 `setting.Tasks[]`（`MdkSetting.TaskConfig`）的**专用编辑窗**，比主窗口右侧属性面板更聚焦：

- 任务切换、脏标记、校验
- 类型说明与参数模板
- 一键写回工作区内存（仍需主窗口「保存」落盘）

不做运行时启停（在线调度见 WinForms `TaskManagerForm` / HTTP `/api/task/*`）。

## 布局

| 区域 | 内容 |
|------|------|
| 顶栏 | Task 下拉、脏标记、参数模板 / 校验 / 应用到工作区 |
| 左 | Name、Type、DriverId、IntervalMs、Parameters 表 + JSON 预览 |
| 右 | 类型说明 + 实时校验结果 |
| 底 | 操作日志 |

## 字段与类型

| Type | DriverId | 常用 Parameters |
|------|----------|-----------------|
| `pollDriver` | 必需 | `varPrefix`（可选） |
| `operation` | 可空 | `gpioDeviceId`（建议；兼容旧键 `deviceId`） |
| `cycle` | 可空 | 一般仅 IntervalMs |
| `motion` | 必需 | 自定义键 → `SetParam` |
| `pnpCycle` / `pnpConveyor` | 视插件 | 扩展参数 |

类型目录：`ConfigTypeCatalog.TaskTypes`。工厂：`RuntimeTaskFactory`。

## 应用语义

1. 校验：Name / IntervalMs 错误阻断；缺 Driver、gpio 未匹配等为警告（可确认后继续）。
2. 写回：修改当前 `TaskConfig` 引用（或无选中时新增）。
3. 回调 `onApplied` → 主窗口 `RefreshTreeKeepingSelection`。
4. **不自动 Save**；提示用户主窗口保存。

## 修改指引

1. **增类型说明**：改 `DescribeType` / `GetParameterPreset`。
2. **在线调试**（Pause/Resume）：另开运行时会话窗，勿与本编辑窗混写 setting。
3. **新建任务**：当前以编辑已有为主；无选中且 Name 唯一时可 Add。完整新建仍可用主窗口「新建组件」。
4. **样式**：沿用 `App.xaml` 资源键。

## 相关

- 主窗口属性 `ApplyTask`：`ConfigWorkspace.ApplyTask`
- 遗留 WinForms：`TasksConfigForm`
