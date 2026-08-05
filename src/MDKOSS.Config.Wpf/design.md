# MDKOSS.Config.Wpf 界面设计

离线配置主界面。WinForms 在线壳仍见 [MDKOSS.Config/design.md](../MDKOSS.Config/design.md)。

## 布局（四区 + 菜单）

```text
┌─ Menu ──────────────────────────────────────────────────────────────────┐
│ 文件 | 编辑 | 配置 | 数据库 | 帮助                                       │
├──────────┬────────────────────────────────┬─────────────────────────────┤
│          │  当前模块: Drivers             │                             │
│  Tree    │  ┌──────────────────────────┐  │  属性编辑                   │
│  模块    │  │ 组件列表 (DataGrid)      │  │  Id / Type / Enabled …     │
│  └组件   │  │  右键: 新建/复制/删除/   │  │  Parameters (原文)          │
│          │  │        上移/下移/编辑    │  │  [应用属性]                 │
│          │  └──────────────────────────┘  │                             │
├──────────┴────────────────────────────────┴─────────────────────────────┤
│ Status: 路径 | 模块 | 选中组件 | 计数                                      │
└─────────────────────────────────────────────────────────────────────────┘
```

| 区域 | 职责 |
|------|------|
| **菜单栏** | 全部配置操作入口（打开/保存/导入导出、增删改排序、导出 SQLite） |
| **左侧树** | **模块**（Drivers/Devices/…）与其下 **组件** 导航；点模块刷中部列表，点组件同时选中该行 |
| **中部列表** | 当前模块内组件概览列；**右键菜单**快捷编辑 |
| **右侧属性** | 选中组件的可编辑字段；「应用属性」写回内存模型，Save 才落盘 |

命令不堆在中部工具条；中部仅保留模块标题 + 列表。

## 菜单

| 菜单 | 命令 |
|------|------|
| 文件 | **打开**（JSON 或 DB）/ 重新加载 / **保存**（写回当前格式） |
| 文件 | **另存为 JSON** / **另存为数据库**（写出并切换当前文档） |
| 文件 | **导出为 JSON** / **导出为数据库**（写出副本，不切换文档） |
| 编辑 | 新建（弹窗）/ 复制 / 删除 / 上移 / 下移 / 应用属性 |
| 模块 | **导出/导入当前模块**（合并或替换）；快速定位 |
| 数据库 | 刷新表预览 |

## 新建与属性编辑

- **新建**：弹窗填写 Id/Name/Type/DriverId/Enabled 与 Parameters 表；Type、DriverId 为可编辑 ComboBox。
- **右侧属性**：Type / DriverId 下拉；Parameters 以 Key-Value 表编辑（可展开原始 JSON）。
- **模块导入导出**：对当前模块行集写出/读入 JSON。

## 文档模式

| 打开 | 保存 | 导出 |
|------|------|------|
| `*.setting.json` / `*.json` | 写回该 JSON | 导出为 `.db` |
| `*.db` | 写回该 DB（`MdkConfigStore.ExportSetting`） | 导出为 `.json` |

状态栏与标题显示 `[JSON]` / `[DB]` 与当前主文档路径。

## 导航树模块

| 模块 Tag | 组件来源 | 列表列 |
|----------|----------|--------|
| `Drivers` | `setting.Drivers` | Id, Type, Enabled |
| `Devices` | `setting.Devices`（**不含** platform 族） | Id, Name, Type, DriverId, Enabled |
| `Axis` | type=`axis` 设备 | Id, Name, DriverId, Enabled |
| `Platform` | platform 族（`platform` / `xy`…`xyzuvw`），**不挂在 Devices 树下** | Id, Name, Type, Kind, DriverId |
| `Gpios` | gpio/vio 点位投影 | DeviceId, Alias, Direction, Route |
| `Tasks` | `setting.Tasks` | Name, Type, DriverId, IntervalMs |
| `Vars` | `setting.Vars` | Key, Value |
| `Recipes` | `setting.Recipes` | Id, Name, Description |
| `SysConfig` | 工程顶层键 | Key, Value |
| `Database` | 左树选表 → 中部显示该表行（可编辑）→ 右侧列属性 Key/Value；应用写回 SQLite |

树叶子为组件实例；`Gpios` / `Vars` / `SysConfig` / `Database` 以派生行或键值作为“组件”。

## 交互链路

1. 树选 **模块** → 中部加载该模块列表；右侧清空或显示模块摘要。
2. 树选 **组件** 或中部点选一行 → 右侧绑定该组件属性。
3. 右键 / 编辑菜单 → 新建、复制、删除、排序；新建后右侧聚焦编辑。
4. 右侧改字段 → **应用属性** 回写内存；**文件 → 保存** 写回当前打开的 JSON 或 DB。
5. **导出为** 另一种格式写出副本；**另存为** 切换当前文档。

## 调试界面（独立窗）

离线配置与联调分离：主窗口仍只编辑 setting；联调通过 **调试** 菜单或列表右键打开独立窗口，按需连接驱动（不启动完整 Monitoring HTTP，除非后续扩展）。

| 窗口 | 文档 | 能力摘要 |
|------|------|----------|
| Driver | [Debug/DriverDebug.md](./Debug/DriverDebug.md) | IO 读写、参数、配置路径 |
| Axis | [Debug/AxisDebug.md](./Debug/AxisDebug.md) | 状态、回零、点动、速度/位置移动 |
| Platform | [Debug/PlatformDebug.md](./Debug/PlatformDebug.md) | 多轴状态、选中轴运动测试 |
| CameraDev | [Debug/CameraDevDebug.md](./Debug/CameraDevDebug.md) | 打开/关闭/采集（含 extcamera） |
| Task | [Debug/TaskDebug.md](./Debug/TaskDebug.md) | Name/Type/DriverId/Interval/参数编辑与校验 |
| Flow | [Debug/Flow/FlowEditor.md](./Debug/Flow/FlowEditor.md) | 节点图编辑 `parameters.flowJson`；运行时 FlowTask 执行 |

共享逻辑：`Debug/DebugSession.cs`。启动时 `MdkExtensionHost.DiscoverAndRegister` 加载 `plugins/`。

## 属性编辑策略

- 固定字段按模块类型显示（Driver / Device / Task / Recipe / Var / Sys）。
- `parameters` / recipe vars 以 JSON 文本编辑；应用时解析失败则提示，不写回。
- GPIO 点位：编辑 route（`driverId:address`）与 alias；回写所属设备 `parameters`。
