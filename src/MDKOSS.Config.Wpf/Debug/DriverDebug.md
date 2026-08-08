# Driver 调试界面说明

> 实现：`DriverDebugWindow.xaml` / `DriverDebugWindow.xaml.cs`  
> 共享：`DebugSession.cs`（`ConnectedDriver`、`IoBitGrid`）

## 定位

对单个 `setting.Drivers[]` 条目做联调：查看参数与配置路径、连接真实/仿真驱动、读写 DI/DO。

**不写回**配置文件；参数表仅作用于本会话。持久化请回主窗口右侧属性面板 → 应用属性 → 保存。

## 入口

- 菜单 **调试 → Driver 调试…**
- Drivers 模块选中行后，右键 **打开调试界面…**

构造：`new DriverDebugWindow(workspace, preferredDriverId?)`

## 布局

| 区域 | 内容 |
|------|------|
| 顶栏 | Driver 下拉、连接/断开、刷新、连接状态徽章 |
| 左栏 | Id/Type/Enabled、工程 DocumentPath、驱动配置路径键+路径、Parameters 表 |
| 右栏 | IO Group、读 DI / 读 DO / 写 DO 字 / 写选中位、位表 |
| 底栏 | 操作日志 |

## 行为与 API

| UI | 调用 |
|----|------|
| 连接 | `DriverFactory.Create(type)` → `IDriver.Initialize(config)` |
| 读 DI | `TryReadDi` 连续 group，按 `inBits`（vio 默认 128）展开 |
| 读 DO | `TryReadDo` 连续 group，按 `outBits`（vio 默认 128）展开 |
| 写 DO 字 | 按位表分组写回各 group |
| 写选中位 | `WriteDoBit(group, index, value)` |

配置路径键默认候选：`configPath` / `configFile` / `cfgPath` / `cfgFile` / `cfg` / `iniPath` / `xmlPath` / `dllPath`。

## 修改指引

1. **增 IO 能力**（如模拟量）：在右栏加控件，直接调 `IDriver.TryRead` / `Write`。
2. **增驱动专属参数页**：按 `TypeBox` 切换 `UserControl`，仍用会话 `ConnectedDriver`。
3. **允许写回配置**：在「应用参数」中调用 `workspace` 更新对应 `DriverConfig` 并提示用户保存（当前刻意不做）。
4. **样式**：沿用 `App.xaml` 的 `AccentBrush` / `PanelBrush` 等资源。

## 依赖

- 启动时 `MdkExtensionHost.DiscoverAndRegister`（见 `App.xaml.cs`）
- `MdkPlugins.targets` 将 `plugins/*.dll` 复制到输出目录
