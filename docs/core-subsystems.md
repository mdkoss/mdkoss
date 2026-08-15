# 核心子系统

本文描述 MDKOSS.Core 内的驱动、设备、任务、变量与配方子系统。

## 驱动层（Driver）

### IDriver 契约

`core/drivers/idriver.cs` 定义统一接口：

- `Initialize(DriverConfig)` — 读取 parameters
- 连接/在线状态
- 数字 IO、轴等读写（按驱动能力实现）
- 单轴 `MoveAxisTrap` / `Jog` / `Home` / `Stop`
- 多轴插补（默认返回 false）：`MoveLine` / `MoveArc` / `TryGetInterpState`。SIM 在 10ms 定时器上模拟路径；DMC 走 `dmc_line_unit` / `dmc_arc_move_center_unit`；GTS 走坐标系 `GT_LnXY` / `GT_ArcXYC`
- `Dispose()` — 释放 native/硬件资源

### DriverFactory

内置注册（`driver_factory.cs`）：

```csharp
["gts"] = () => new DrvGts(),
["sim"] = () => new DrvSim(),
```

`MdkRuntime.BootstrapDrivers` 对 `enabled` 驱动调用 `Create` → `Initialize`，存入 `_drivers` 字典。

### 驱动与设备关系

- **单驱动设备**（`axis`、`cameradev`、部分 `vio`）：设备持有单个 `IDriver` 引用
- **多驱动设备**（`gpio`）：设备持有 `IReadOnlyDictionary<string, IDriver>`，按绑定点路由
- **平台设备**：每个轴字母对应一个 `AxisDevice` + 各自驱动

## 设备层（Device）

### MDeviceBase

`core/mdev.cs` 中所有设备的基类，提供：

- `Id`、`Name`、`Initialize()`、`Start()`、`Stop()`、`Dispose()`
- 与 `MVarStore` 的状态同步
- 快照贡献（供 `GetSnapshot` 聚合）

### Core 内置设备

| 类型 | 类 | 要点 |
|------|-----|------|
| GPIO | `GpioDevice` | 建议单实例；默认挂载全部非 vio 驱动；别名 → `driverId|di.gpi.bit.n` / `do.gpo.bit.n` |
| VIO | `VioDevice` | 虚拟地址，读写走单驱动内存语义 |
| Axis | `AxisDevice` | 单轴运动与状态 |
| Platform | `PlatformDevice` | 由多个 `AxisDevice` 组成，`MPlatformKind` 描述轴布局 |
| Camera | `CameraDevDevice` | 占位/扩展点 |

### 设备实例化流程

`MdkRuntime.BootstrapDevices` 按 `type` 分支：

1. `gpio` → `BuildGpioDevice`
2. `vio` → `BuildVioDevice`
3. 平台族 → `BuildPlatformDevice`
4. `DeviceExtensionRegistry.TryCreate` — Extensions 类型
5. 否则 `axis` / `cameradev` 等 Core 内置 switch

### 设备动作（Device Action）

统一入口：`MdkRuntime.ExecuteDeviceAction(deviceId, action, parameters)`

分发顺序：

1. `DeviceActionRegistry` — Extensions 注册的执行器（serial/tcp）
2. 内置 switch：`GpioDevice`、`VioDevice`、`AxisDevice`、`PlatformDevice`

HTTP `POST /api/devices/{id}/action` 与此路径一致。

## 任务层（Task）

### MTaskScheduler

- 按 `intervalMs` 调度已注册任务
- `Start()` / `StopAsync()` 与运行时生命周期绑定
- 停止时先停调度器，避免关机过程中设备仍被任务访问

### MTaskBase

任务实现继承基类，在 `RuntimeTaskFactory` 中按 config `type` 创建。

### TaskBootstrapContext

创建任务时注入：

- `_drivers`、`_devices`、`Vars`
- `GetSnapshot`、`ListTasks` 委托

便于任务读取全局状态或触发监控逻辑。

### 内置任务

| 任务 | 文件 | 用途 |
|------|------|------|
| PollDriverTask | `core/mtask.cs` | 轮询驱动在线/心跳 |
| TaskOperation | `tasks/task_operation.cs` | 操作步骤序列 |
| TaskCycle | `tasks/task_cycle.cs` | 周期循环逻辑 |
| MotionTask / TaskMotionTask | `tasks/task_motion.cs` | 运动任务基类与可配置类型（type=`motion`）；运动走轴/平台设备，IO 走 GpioDevice |

## 变量中心（MVarStore）

`core/mvar.cs` 提供线程安全的：

- `Set` / `Get` / `TryGet`
- `Snapshot()` — 导出全部键值

用途：

- 配置种子 vars
- 任务与设备间共享状态
- 配方应用后的参数集
- 监控快照中的 `Vars` 字段
- 排单列表缓存键 `order.list`

## 配方管理（MdkRecipeManager）

`core/mrecipe.cs` 管理 `MdkSetting.Recipes`：

- `BootstrapActiveRecipe()` — 启动时应用 `activeRecipeId`
- `TryApplyRecipe(id)` — 将 recipe.vars 写入 `MVarStore`，更新 `recipe.activeId` / `recipe.activeName`
- `TryCaptureRecipe(id)` — 从当前 vars 捕获 recipe 作用域键
- 校验：recipe vars 键必须在 `recipeVarKeys` 定义范围内

与 SQLite 双向同步见 [data-persistence.md](./data-persistence.md)。

## 错误处理

`mdk_errors.cs` 定义 `MdkErrorCode` 与 `MdkException`，用于：

- 不支持的驱动/设备类型
- GPIO 驱动作用域无效
- 重复任务名
- 配置解析失败

桌面模式加载配置失败时，`Program` 弹窗并写日志。

## 扩展点小结

| 注册表 | 扩展方式 |
|--------|----------|
| `DriverFactory` | 新硬件驱动类型 |
| `DeviceExtensionRegistry` | 新设备类型（通常放 Extensions） |
| `DeviceActionRegistry` | 新设备的 action 处理器 |
| `RuntimeTaskFactory` | 新任务类型 |
| `MonitoringModuleRegistry` | 新 HTTP API 模块 |

Extensions 侧用法见 [extensions.md](./extensions.md)。
