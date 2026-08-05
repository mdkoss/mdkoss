# Axis 调试界面说明

> 实现：`AxisDebugWindow.xaml` / `AxisDebugWindow.xaml.cs`

## 定位

对 `type=axis` 设备联调：状态轮询、使能、回原点、按住点动、速度连续运动、位置梯形运动。

通过设备的 `DriverId` 打开 `IDriver`，轴号来自 `parameters.axis`（可界面改）。

## 入口

- 菜单 **调试 → Axis 调试…**
- Axis 模块选中行后，右键 **打开调试界面…**

## 布局

| 区域 | 内容 |
|------|------|
| 顶栏 | Axis 下拉、轴号、连接/断开、刷新 |
| 状态区 | 使能、Status Word、指令/编码器位置、速度；使能/去使能/停止 |
| 左：参数 | 速度、加减速、目标位置、回零模式 |
| 右：动作 | 回原点、点动±、速度移动、位置移动 |
| 底栏 | 日志 |

## API 映射

| UI | `IDriver` |
|----|-----------|
| 刷新状态 | `IsAxisEnabled` / `TryGetAxisStatus` / `TryGetAxisPrfPosition` / `TryGetAxisEncPosition` / `TryGetAxisVelocity` |
| 使能/去使能 | `EnableAxis` / `DisableAxis` |
| 停止 | `Stop(1 << axis)` |
| 回原点 | `MoveAxisHome(axis, homeMode, vel, acc, dec)` |
| 点动（按住） | `MoveAxisJog`；松开 `Stop` |
| 速度移动 | `SetAxisVelocity` + `MoveAxisJog`（需手动停止） |
| 位置移动 | `MoveAxisTrap(axis, target, vel, acc, dec)` |

连接后以 **200ms** 轮询状态。

## 修改指引

1. **改轮询间隔**：构造函数里 `_pollTimer.Interval`。
2. **软限位/安全互锁**：在 `StartJog` / `PosMove_Click` 前加校验。
3. **走设备层 `AxisDevice.MoveTo`**：可改为启动轻量 `MdkRuntime` 后 `ExecuteDeviceAction(..., "move")`（当前直接打驱动更贴近板卡调试）。
4. **多轴同卡**：轴号框已独立；可从 parameters 增加 `axisNo` 别名（见 `DebugUi.ParseAxisIndex`）。

## 注意

- 不写回 setting；断开时会尝试 `Stop`。
- 仿真驱动 `sim` 可在无硬件时验证 UI 流程。
