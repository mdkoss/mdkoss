# MDKOSS.Config 配置界面设计指引

> 本文件是 `MDKOSS.Config` 的 UI 设计与实现约定。架构总览见 [docs/gui.md](../../docs/gui.md)，历史参考见 [docs/winform-epson-rc-design.md](../../docs/winform-epson-rc-design.md)，JSON 字段见 [docs/configuration.md](../../docs/configuration.md)。

## 1. 目标与边界

MDKOSS.Config 是**工程配置 + 运行时巡检**的桌面工具，风格对齐 EPSON RC+ 一类工程软件，但底层仍是轻量 JSON（`*.setting.json`），不引入独立工程数据库或 IDE 插件体系。

| 做 | 不做 |
|----|------|
| 离线编辑 setting JSON | 替代 CEF / 浏览器 HMI |
| 子系统级导入导出、备份、保存前校验 | 改动运行时模型或 JSON schema（除非明确需求） |
| 在线 Device / Task / I/O Monitor | 把在线操作写回配置文件（除显式「打开配置」流程） |
| 类型化参数预设 + 原始 parameters 回退 | 用复杂可视化编辑器掩盖原始参数 |

**硬性分离**：离线配置编辑（读写文件）与在线监控（读 `MdkRuntime` 快照）必须分窗体、分入口，避免同一页面混用两种数据源。

## 2. 设计原则

1. **工程导向**：始终可见 setting 路径、项目名、运行状态；配置资源按子系统可发现。
2. **五区布局**：顶部菜单栏 → 中左树形导航 → 中部详情（表格或框架结构图）→ 中右属性页 → 底部状态栏；命令进菜单，编辑进属性页，列表/关系进中部。
3. **I/O 一等公民**：Labels、硬件/虚拟点、监控分开展示；Labels 可单独导入导出。
4. **操作可发现**：Backup / Restore、Import / Export、Diagnostics 放在顶部菜单（必要时辅以快捷键），不依赖散落按钮条。
5. **兼容优先**：UI 只是 JSON 的投影；保存结果须与 `MdkSetting` / 现有 `parameters` 形状兼容。
6. **迁移友好**：统一入口是 `ComponentConfigForm`；遗留 `*ConfigForm` 可保留但不扩展新能力。

## 3. 窗体分层

```text
MainForm（宿主总览，同一套五区骨架）
├── ComponentConfigForm     离线：统一配置管理（可独立窗或嵌入中部工作区）
├── DeviceManagerForm       在线：设备状态
├── TaskManagerForm         在线：任务状态 / 启停
├── IoMonitorForm           在线：I/O 点 live 刷新
└── Diagnostics 导出        在线：setting + 快照 + 日志打包

遗留（迁移期，不再作为主入口）：
GpioConfigForm / AxisConfigForm / PlatformConfigForm / DevsConfigForm / TasksConfigForm
```

### 3.0 统一布局骨架

MainForm 与 ComponentConfigForm 共用同一视觉骨架（工程软件 IDE 式）：

```text
┌─ MenuStrip ─────────────────────────────────────────────────────────────┐
│ File | Edit | View | Config | Tools | Diagnostics | Help                 │
├────────────┬──────────────────────────────┬─────────────────────────────┤
│            │                              │                             │
│  TreeView  │   Center Workspace           │   Property Grid / Panel     │
│  导航树    │   · 表格（行集编辑/只读）     │   选中节点或选中行的属性     │
│            │   · 或框架结构图（关系视图）   │   类型化字段 + 原始 JSON 回退 │
│            │                              │                             │
├────────────┴──────────────────────────────┴─────────────────────────────┤
│ StatusStrip: setting 路径 | 模式(离线/在线) | 选中项 | Drivers/… 计数      │
└─────────────────────────────────────────────────────────────────────────┘
```

| 区域 | 控件倾向 | 职责 |
|------|----------|------|
| 顶部菜单栏 | `MenuStrip`（可附 `ToolStrip` 快捷项，但命令以菜单为准） | 文件、编辑、视图切换、配置命令、在线工具、诊断 |
| 中左导航树 | `TreeView` | 工程资源发现：子系统、分类节点、可选叶子实例 |
| 中部工作区 | `DataGridView` **或** 框架结构图画布 | 当前节点的行集表格，或 Driver↔Device↔Task 关系结构图 |
| 中右属性页 | `PropertyGrid` / 自定义属性面板 | 编辑选中对象字段与 `parameters`；不在中部表格内摊开全部细节 |
| 底部状态栏 | `StatusStrip` | 路径、离线/在线模式、选中摘要、行数统计 |

交互主链路：

1. 树选中节点 → 中部切换为对应表格或结构图；属性页显示节点级摘要（或清空待选行）。
2. 中部选中一行 / 结构图选中一节点 → 属性页绑定该对象，就地编辑。
3. 属性页提交（Apply 或失焦提交策略二选一，全局统一）→ 回写内存模型；Save 才落盘。
4. 视图菜单在「表格」与「框架结构图」之间切换；结构图只读浏览关系，编辑仍走属性页。

约束：

- 中部表格以概览列为主（id、type、enabled 等）；长文本 / 嵌套 `parameters` 进属性页。
- 框架结构图展示引用关系（如 Device→Driver、Task→Device），不替代 JSON 编辑器。
- 离线编辑与在线监控可共用骨架，但数据源与可写范围必须由模式区分（状态栏标明）。

### 3.1 MainForm

职责：运行态总览 + 打开各工具；布局遵循 §3.0。

| 区域 | 内容 |
|------|------|
| 菜单 | **File**：Open Setting、Reload Runtime…；**View**：表格 / 结构图、面板显隐；**Tools**：Config Manager、Device / Task / I/O Manager；**Diagnostics**：导出支持包；**Help** |
| 中左树 | Project、Runtime、Drivers、Devices、Tasks、Variables、History（只读资源树，点击切换中部） |
| 中部 | 默认只读表格（Drivers / Devices 快照）；可选框架结构图（驱动—设备—任务关系 + 连接状态着色） |
| 中右属性页 | 选中运行时对象的只读属性（状态、driverConnected、最近错误等）；在线可写项（如任务 Pause）放属性页命令或 Tools 菜单 |
| 状态栏 | setting 路径、Running/Stopped、项目名、快照刷新时间 |

定时刷新：`MdkRuntime.GetSnapshot()`（与 HTTP 监控同源语义）。

约束：

- 切换 setting 文件后应提示重启或重新加载运行时，避免「文件已改、内存仍旧」的静默不一致。
- MainForm 中部不做完整配置编辑；深度编辑走 Config Manager（同一骨架的离线模式）。

### 3.2 ComponentConfigForm（配置管理主界面）

职责：离线编辑 `*.setting.json`；布局遵循 §3.0，菜单与属性页可写。

#### 菜单约定

| 菜单 | 命令 | 行为 |
|------|------|------|
| File | Reload | 从磁盘重载，放弃未保存编辑 |
| File | Save（Ctrl+S） | 校验通过后写回 `_settingPath` |
| File | Backup | 写入带时间戳的备份文件 |
| File | Import / Export Setting | 整份 `MdkSetting` JSON |
| File | Import / Export Current | 仅当前树节点对应子系统行集 |
| Edit | Add / Duplicate / Delete / Move Up / Down | 作用于中部表格当前行 |
| Edit | Apply Param Preset | 按选中行 type 填充 parameters 模板 |
| View | Table / Structure Diagram | 中部在表格与框架结构图间切换 |
| View | Properties | 显示/隐藏中右属性页 |

#### 导航树 → 中部 / 属性页

| 树节点 | 中部（表格） | 中部（结构图，可选） | 属性页编辑内容 |
|--------|--------------|---------------------|----------------|
| Project | 工程摘要卡片或单行键值 | 工程根节点 | `projectName`、`cycleMs`、`monitoringPrefix`、`activeRecipeId` |
| Components → Drivers | Drivers 网格 | Driver 节点 | id / type / enabled / parameters |
| Components → Devices | Devices 网格 | Device→Driver 边 | id / name / type / driverId / enabled / parameters |
| Components → I/O Labels | Labels 网格 | （通常仅表格） | alias / direction / driver / address / description |
| Components → Tasks | Tasks 网格 | Task→依赖边 | name / type / interval / enabled / parameters |
| Components → Variables | Vars 网格 | （通常仅表格） | key / value |
| Recipes → Recipe Keys | Keys 网格 | — | 参与配方的 var 键 |
| Recipes → Presets | Recipes 网格 | Recipe→Vars 覆盖 | 配方 id 与变量覆盖 |
| Import / Export | 操作说明页 | — | 指向 File 菜单命令 |

行操作统一经 **Edit** 菜单（及可选快捷工具条）：Add / Duplicate / Delete / Up / Down；中部保留搜索过滤框。

### 3.3 在线工具窗

在线工具优先嵌入 MainForm 五区骨架（树切到对应节点即可）；独立浮动窗保留时，仍建议「中部表格 + 右侧属性 + 底栏」，避免回到纯按钮条布局。

| 入口（Tools / 树节点） | 数据源 | 刷新 | 可写操作 |
|------------------------|--------|------|----------|
| Device Manager | 快照 Devices | ~1s | 无（只读巡检；细节在属性页） |
| Task Manager | 任务快照 API | ~1s | Pause / Resume / Stop（属性页或 Edit 菜单） |
| I/O Monitor | 设备 GPIO/VIO 点 | ~0.5s | 仅安全范围内的手动 toggle（若开放；属性页或行命令） |

禁止在这些视图中直接改 setting JSON。

## 4. 参数编辑策略

参数编辑默认落在**中右属性页**，中部表格只保留概览列。

优先级（从易到难）：

1. **类型化属性字段**：按 driver / device / task type 在属性页展示稳定键（含 Param Preset 一键填充）。
2. **结构化子表**：I/O Labels、GPIO/VIO 映射等在中部用行编辑，选中行后属性页显示扩展字段。
3. **原始 parameters 文本**：属性页高级区始终保留；解析失败时阻止保存并给出明确错误。

新增设备/驱动类型时：

- 先在 Core / Extensions 注册类型与 `parameters` 约定；
- 再在属性页增加预设模板与类型化字段；
- 有稳定字段后再提升为属性页专用编辑器，避免过早改中部表格列集合。

## 5. 校验与导入导出

保存前至少校验：

- id / name / key 非空；
- driver / device / task / recipe id 唯一；
- 设备引用的 `driverId` 存在（或明确允许空）；
- 任务 interval 合法；
- `parameters` 可解析为 JSON 对象。

导入导出层级：

1. **工程级**：整份 setting；
2. **子系统级**：当前 Tab 行数组；
3. **备份**：同结构副本，文件名含时间戳；
4. **Diagnostics**（MainForm）：setting + 运行时快照 + 日志，面向支持排查。

导入后必须重新走校验；Restore 覆盖前应可取消。

## 6. 视觉与交互约定

- 技术栈：WinForms（.NET 8 Windows）用于在线壳；**离线配置编辑优先使用** [MDKOSS.Config.Wpf](../MDKOSS.Config.Wpf/design.md)。
- 主配置窗建议最小约 960×600，默认约 1280×760；导航栏约 210，属性页约 280，可拖拽但保留面板下限。
- 命令以菜单栏为准；中部列表支持右键快捷编辑，不使用顶栏按钮堆。
- 只读监控网格与可编辑配置属性页视觉区分：监控窗标题带 Monitor，配置窗带 Config；状态栏标明 Offline / Online。
- 错误用 `MessageBox` 明确字段原因；状态栏显示路径、模式、选中项与计数。
- 快捷键：`Ctrl+S` 保存；`Ctrl+N` / `Ctrl+D` 增删复制行（Edit 菜单）。

## 7. 源码映射

| 关注点 | 文件 |
|--------|------|
| 入口 | `Program.cs` → `RuntimeHost` → `MainForm` |
| 五区骨架 | `WorkspaceShell.cs` |
| 框架结构图 | `StructureDiagramPanel.cs` |
| 配置总管 | `ComponentConfigForm.cs` |
| 读写辅助 | `ConfigFormHelpers.cs` |
| Device / Task / I/O 浮动窗 | `DeviceManagerForm.cs` / `TaskManagerForm.cs` / `IoMonitorForm.cs` |
| 遗留单页 | `GpioConfigForm.cs` 等 |
| 示例配置 | `configs/sample.setting.json` |

命名空间：`MDKOSS.Gui`（历史命名；程序集为 `MDKOSS.Config`）。

## 8. 变更检查清单

改配置 UI 时按序确认：

- [ ] 是否仍保持「离线编辑 / 在线监控」分离？
- [ ] 保存结果是否仍能被 `MdkSetting.Load` 正确反序列化？
- [ ] 子系统 Import/Export 与整包 Import/Export 是否都可用？
- [ ] 保存前校验是否覆盖新增字段？
- [ ] 导航树节点与详情页标题是否一一对应？
- [ ] 是否更新了本文件与 [docs/gui.md](../../docs/gui.md) 中过时描述？

## 9. 后续方向（非承诺）

以下仅作演进参考，实施前需单独开需求：

- 弱化并最终移除遗留 `*ConfigForm` 入口；
- I/O Monitor 按标签过滤、方向分组；
- 在线任务 Pause/Resume 接入属性页命令；
- 参数编辑从「预设 + 原文」演进为可插拔的类型编辑器面板。
