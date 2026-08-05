# Platform 调试界面说明

> 实现：`PlatformDebugWindow.xaml` / `PlatformDebugWindow.xaml.cs`

## 定位

对平台族设备（`platform` / `xy` / `xyz` / `xyzu` / `xyzuv` / `xyzuvw`）联调：按 `kind` 展开轴状态表，对选中轴执行回原点、点动、速度移动、位置移动。

## 入口

- 菜单 **调试 → Platform 调试…**
- Platform 模块选中行后，右键 **打开调试界面…**

## 布局

| 区域 | 内容 |
|------|------|
| 顶栏 | Platform 下拉、kind 徽章、连接/断开、刷新 |
| 左 | 轴状态表（字母、轴号、DriverId、在线/使能/位置/速度） |
| 右 | 当前轴运动参数与动作按钮；全部轴停止 |
| 底 | 日志 |

## 轴解析

与运行时 `BuildPlatformDevice` 一致：

1. `PlatformDeviceParameterSet.ParseKindOrDefault` 得 `MPlatformKind`
2. `kind.AxisLetters()` 得 X/Y/Z/…
3. `ResolveAxisDriverId(parameters, letter, device.DriverId)` 得每轴驱动
4. 轴号按字母顺序从 `0` 递增（同卡多轴约定）

连接时对涉及到的每个 `DriverId` 调用 `ConnectedDriver.Open`（`MultiDriverBag`）。

## API

与 [AxisDebug.md](./AxisDebug.md) 相同，作用在选中轴的 `IDriver` + `AxisIndex`。

## 修改指引

1. **改为平台级使能**：可对所有轴循环 `EnableAxis`，或接入 `PlatformDevice.SetMotion`（需 `MdkRuntime`）。
2. **示教点**：可参考 CEF `debug_platform.html` / `_docs/debug_platform.md`，底栏加 Teach 选项卡调 `/api/teach` 或 `MdkDataStore`。
3. **轴号映射自定义**：若板卡轴号非 0..n-1，在 parameters 增加 `axisIndex.X` 等并在 `BindSelected` 解析。
4. **矩阵点动按钮**：可仿 Web 页为每轴生成 ± 按钮，当前为「选中轴 + 通用 ±」。

## 注意

- 不写回 setting。
- 多驱动平台断开时 `MultiDriverBag` 统一 Dispose。
