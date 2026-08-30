# MDKOSS.UI.WPF

Prism + WPF 主界面宿主。职责与 CEF 的 `index.html` / `popup_*` / `monitor_*` / `debug_*` / `man_*` 对齐，但是**原生 WPF**，不嵌 HTML、不走监控 HTTP 拼页面。

产品版本见根目录 [readme.md](../../readme.md) 与 [src/README.md](../README.md)。HTML 对照表见 [MDKOSS.Cef/views/README.md](../MDKOSS.Cef/views/README.md)。

驱动、设备、任务、配方、报警来自 [`configs/sample.setting.json`](configs/sample.setting.json)。可用 `--setting path\to\file.json` 换配置。

## 运行

```bash
dotnet run --project src/MDKOSS.UI.WPF/MDKOSS.UI.WPF.csproj -c Debug
```

VS Code：`.NET Core Launch (UI.WPF)`。Visual Studio 将 `MDKOSS.UI.WPF` 设为启动项目。

默认监控地址见 JSON 的 `monitoringPrefix`（当前 `http://127.0.0.1:5083/`，避免与 CEF Sample 的 5081 冲突）。

---

## 1. 分层原则（不要混用）

与 CEF 相同的四层。页面 id 用**小写 + 下划线**，与 HTML 文件名一致，便于对照和跳转。

| 层 | 入口 | 写操作 | 用户 | WPF 落点 |
|----|------|--------|------|----------|
| 生产 HMI | `HomeView`（对应 `index.html`） | 仅启停 / 复位 / 选工单 / 选配方 | 操作员 | `Views/HomeView` + `ShellView` 顶底栏 |
| 二级弹窗 | `popup_*` | 极少 | 操作员 | `Views/Dialogs/*` + `IDialogService` |
| 监控 | `monitor_*` | 否 | 工艺 / 维护 | `Views/Monitor` + `ViewModels/Tools/Monitor` |
| 调试 | `debug_*` | 是（危险操作先确认） | 调试 / 维护 | `Views/Debug` + `ViewModels/Tools/Debug` |
| 配置 | `man_*` | 改内存 `MdkSetting`，保存到磁盘 | 工程师 | `Views/Man` + `ViewModels/Tools/Man` |

```text
Home ──Dialogs──► ToolHost(monitor_*) ──► debug_*
                                 └──────────► man_*
```

- 完整组态（视觉管线节点、HMI 画布）仍以 **Config.Wpf** / **Cef.Extensions** 为准。本项目不实现 `man_hmi`。
- 不要再往通用 `ToolPageView` 加业务。每种组件一张独立 View + ViewModel。
- 工具页数据直接读 `MdkRuntime`（`IRuntimeUiService`），不要为了画界面去 `fetch /api/*`。

---

## 2. 启动与导航骨架

```text
App (PrismApplication)
  ├─ 发现插件 → 加载 setting → new MdkRuntime → Start
  ├─ RegisterInstance(runtime)
  ├─ IRuntimeUiService / IToolNavigator
  └─ RegisterForNavigation / RegisterDialog（页面 id = 导航名）

ShellView
  ├─ 顶栏 / 底栏（仅 ContentMode=home 显示）
  └─ ContentRegion
        ├─ HomeView          订单列表
        └─ ToolHostView      工具顶栏 + ToolContentRegion
              └─ monitor_* / debug_* / man_* 子页
```

| 名称 | 用途 |
|------|------|
| `ContentRegion` | 主界面 ↔ 工具壳 |
| `ToolContentRegion` | 工具壳内的具体子页 |
| `IToolNavigator` | `Navigate(group, page)` / `NavigateByPage(pageId)` / `GoHome()` |
| `ToolCatalog` | 分组与页面目录（顶栏按钮由此生成） |
| `GoToolCommand` | 页内跳转，参数是页面 id，如 `debug_axis` |

进入工具页时 `ShellViewModel.ContentMode = tool`，HMI 顶底栏收起，只留 `ToolHost` 顶栏（回主界面 + 同组页签 + 监控/调试/配置）。

---

## 3. 目录

```text
src/MDKOSS.UI.WPF/
├── App.xaml(.cs)                 # 启动、DI、导航注册
├── configs/                      # 随 exe 复制的样例配置
├── Assets/app.ico
├── Themes/
│   ├── NavyTheme.xaml            # 主界面 / 弹窗（深蓝 HMI）
│   ├── MonitorTheme.xaml         # Tool* 画刷：青绿
│   ├── DebugTheme.xaml           # Tool* 画刷：琥珀
│   ├── ManTheme.xaml             # Tool* 画刷：冷白钢蓝
│   ├── ToolControls.xaml         # 工具页按钮 / KPI / 卡片 / 提示条
│   └── ToolImplicit.xaml         # 工具页 DataGrid / TextBox 隐式样式
├── Infrastructure/
│   ├── ToolCatalog.cs            # 加页先改这里
│   ├── ToolNavigator.cs
│   ├── DeviceKind.cs             # 设备过滤 + ConfirmWrite
│   ├── UiNames.cs                # Region / Dialog 名
│   └── Converters.cs
├── Services/                     # 唯一对 Runtime 的 UI 门面
├── Models/                       # 行模型（不要把 Setting 实体直接绑到 DataGrid）
├── ViewModels/
│   ├── ShellViewModel.cs / HomeViewModel.cs
│   ├── Dialogs/
│   └── Tools/
│       ├── LiveToolViewModel.cs  # 监控 / 调试基类（订阅 SnapshotChanged）
│       ├── Monitor/ Debug/ Man/
│       └── ToolHostViewModel.cs
└── Views/
    ├── ShellView / HomeView
    ├── Dialogs/
    ├── Tools/ToolHostView        # 换主题、嵌 ToolContentRegion
    ├── Monitor/ Debug/ Man/      # 一类组件一个 View
```

命名约定：

| 页面 id | View | ViewModel | 命名空间 |
|---------|------|-----------|----------|
| `monitor_axis` | `Views/Monitor/MonitorAxisView` | `MonitorAxisViewModel` | `*.Views.Tools.Monitor` / `*.ViewModels.Tools.Monitor` |
| `debug_axis` | `Views/Debug/DebugAxisView` | `DebugAxisViewModel` | `*.Views.Tools.Debug` |
| `man_axis` | `Views/Man/ManAxisView` | `ManAxisViewModel` | `*.Views.Tools.Man` |

Prism 导航名必须等于页面 id。`RegisterForNavigation<View, VM>("monitor_axis")`。

---

## 4. 页面目录

### 4.1 主界面与弹窗

| WPF | 对应 HTML | 说明 |
|-----|-----------|------|
| `HomeView` | `index.html` 主区 | 工单列表 |
| `ShellView` 顶/底栏 | `index.html` 顶底栏 | 品牌、弹窗入口、三色灯、启停复位、配方 |
| `DevicesDialog` | `popup_devices` | 设备摘要 |
| `TasksDialog` | `popup_tasks` | 任务摘要 |
| `VarsDialog` | `popup_vars` | 变量只读 |
| `AlarmsDialog` | `popup_alarms` | 活动报警 |
| `OrderDialog` | `popup_order` | 当前工单 |
| `RecipeDialog` | `popup_recipe` | 切换配方 |
| `UserDialog` | `popup_user` | 占位 |
| `AboutDialog` | `popup_about` | 可跳进工具页 |

### 4.2 监控（只读）

| id | 界面要点 |
|----|----------|
| `monitor_runtime` | KPI + 驱动/设备矩阵 + 作业摘要 |
| `monitor_io` | 灯盘 / 明细切换，按设备分组 |
| `monitor_platform` | 平台表 + 选中轴表 |
| `monitor_axis` | 全轴表 + Faceplate |
| `monitor_camera` | 相机表 + Faceplate |
| `monitor_vision` | 视觉 KPI + 流程定义 + `vision.*` 变量 |
| `monitor_task` | 节拍 KPI + 任务表 |
| `monitor_alarm` | 活动 / 目录筛选 |

基类：`LiveToolViewModel`（1s 快照刷新）。写操作只给链接，跳到对应 `debug_*`。

### 4.3 调试（可写）

| id | 界面要点 |
|----|----------|
| `debug_machine` | 启停暂停复位（确认） |
| `debug_axis` | 使能 / 按住点动 / 定位（使能与定位需确认） |
| `debug_platform` | 平台使能 + 分轴 Jog / 步进 |
| `debug_io` | DO 拨动（确认） |
| `debug_driver` | 地址读；写需确认 |
| `debug_serial` / `debug_mysql` / `debug_camera` / `debug_vision` | `ExecuteAction` |
| `debug_alarm` | 确认 / 复位 / 模拟触发 |
| `debug_db` | 工单 / 配方只读一览 |

确认策略与 HTML `confirmWrite` 一致，统一走 `DeviceKind.ConfirmWrite`：

- **需确认**：使能/去使能、绝对定位、强制 IO、驱动位写、启停复位、报警复位/模拟、删除、相机关闭、视觉 run、串口/MySQL 断开
- **无需确认**：点动按住、步进、发送/Query、状态刷新

### 4.4 配置（运行时轻量编辑）

| id | 编辑对象 |
|----|----------|
| `man_machine` | 项目名 / 周期 / 路径等整机字段 |
| `man_driver` / `man_device` / `man_axis` / `man_platform` / `man_gpio` | `MdkSetting` 对应集合 |
| `man_task` / `man_vars` / `man_recipe` / `man_vision` / `man_alarm` | 任务、种子变量、配方、视觉名+默认相机、报警条件 |

列表 + 属性表单。`应用属性` 只写内存；`保存到磁盘` 调 `TrySaveSetting`（`Setting.Save` + 配方入库）。**现场设备实例要重启运行时才重建**，与 HTML `man_editor.js` 相同。

目录页基类：`ManCatalogViewModel`（不要接 `LiveToolViewModel`，避免快照刷新冲掉正在编辑的表单）。

---

## 5. 主题

| 场景 | 资源 | 风格 |
|------|------|------|
| Home / Dialogs | `NavyTheme.xaml` | 深蓝操作员 HMI |
| 监控 | `MonitorTheme` + `ToolControls` | 深工业灰 · 青绿带 · 只读 |
| 调试 | `DebugTheme` + `ToolControls` | 深工业灰 · 琥珀带 · 可写 |
| 配置 | `ManTheme` + `ToolControls` | 冷白 · 钢蓝带 · 编辑 |

`ToolHostView` 按 `GroupId` 合并主题字典。工具页颜色用 **`{DynamicResource ToolXxxBrush}`** 和 `ToolBtnStyle` / `ToolCardStyle` / `ToolNoteStyle`，不要写死海军蓝 `StaticResource`。

改配色：只改对应 `*Theme.xaml` 里的 `Tool*` 画刷，键名三套必须齐全。

---

## 6. 数据怎么取

所有工具页 / 弹窗只依赖 `IRuntimeUiService`：

| 能力 | 方法 |
|------|------|
| 周期快照 | `LatestSnapshot` / `SnapshotChanged` / `Refresh` |
| 工单 / 任务 / 配方 | `ListOrders` / `ListTasks` / `GetRecipeSnapshot` / `TryApplyRecipe` |
| 整机命令 | `SendMachineCommand`（`start` / `stop` / `reset` / `pause`） |
| 轴 / 平台 | `TryAxis*` / `TryPlatform*` |
| IO / 驱动字 | `TryWriteIo` / `TryReadDriver` / `TryWriteDriver` |
| 扩展设备动作 | `ExecuteAction(deviceId, action, params)` → `Runtime.ExecuteDeviceAction` |
| 报警 | `ListActiveAlarms` / `AckAllAlarms` / `ClearAllAlarms` / `TryTriggerDemoAlarm` |
| 保存配置 | `TrySaveSetting` |

设备分类用 `DeviceKind`（`IsAxis` / `IsPlatform` / `IsGpio` / `IsCamera` …），与 HTML `isAxisType` 等规则对齐。

变量键与 CEF 页相同：`machine.*`、`task.operation.*`、`task.cycle.*`、`vision.*`、`recipe.*`。读快照用 `SnapshotReader`。

---

## 7. 二次开发：加一个组件子页

以新增 `monitor_foo` / `debug_foo` / `man_foo` 为例。三层都要独立做，不要复用别的组件表格凑合。

1. **对照 HTML**  
   先读 `src/MDKOSS.Cef/views/monitor_foo.html`（或 debug/man）的区块：页头、提示条、KPI、主表、Faceplate、按钮。WPF 按同一结构排，不要先发明新布局。

2. **登记目录**  
   `ToolCatalog` 对应分组加 `new("monitor_foo", "Foo")`。

3. **ViewModel**  
   - 监控 / 调试：继承 `LiveToolViewModel`，实现 `Reload()`。  
   - 配置：继承 `ManCatalogViewModel`，实现 `Enumerate` / `LoadItem` / `ApplyItem` / `CreateItem` / `RemoveItem` / `DuplicateItem`。  
   - 危险写调用 `DeviceKind.ConfirmWrite`，再调 `IRuntimeUiService`。  
   - 页内跳转：`GoToolCommand` + `CommandParameter="debug_foo"`。

4. **View**  
   `Views/Monitor/MonitorFooView.xaml`（+ `.xaml.cs` 只 `InitializeComponent`）。  
   `prism:ViewModelLocator.AutoWireViewModel="True"`。  
   页头：标题 + 只读/可写/配置徽章；提示条用 `ToolNoteStyle` / `WritableNoteStyle`。

5. **注册导航**  
   在 `App.RegisterTypes` 增加：

   ```csharp
   containerRegistry.RegisterForNavigation<MonitorFooView, MonitorFooViewModel>("monitor_foo");
   ```

6. **不要做的事**  
   - 不要把新页接到 `ToolPageView`。  
   - 不要在 View 里直接 `new MdkRuntime` 或读文件。  
   - 不要为 UI 新增 Core HTTP 路由。  
   - 不要在配置页订阅 `SnapshotChanged` 覆盖未保存编辑。

### 加一个主界面弹窗

1. `Views/Dialogs/XxxDialog.xaml` + `XxxDialogViewModel`（可继承 `LiveDialogViewModel`）。  
2. `DialogNames` 加常量。  
3. `RegisterDialog<XxxDialog, XxxDialogViewModel>(DialogNames.Xxx)`。  
4. `ShellView` 顶栏按钮 → `IDialogService.ShowDialog`。

### 加一种设备过滤

在 `DeviceKind` 增加 `IsXxx(DeviceSnapshot)`，监控 / 调试页用同一谓词，避免各页各写一套 `type` 字符串。

### 宿主里加扩展页（不要改本工程 App）

示例见 [`MDKOSS.UI.WPF.Sample`](../MDKOSS.UI.WPF.Sample/README.md)：

1. 实现 `IWpfUiExtension`，`ToolPage<View, VM>(pageId, group, label)`。
2. `Program` 里先 `WpfUiExtensionHost.Register(...)`，再 `MdkWpfHost.ExtraExtensions` 注册 `IMdkExtension`，最后 `MdkWpfHost.Run`。
3. 主题资源用 `pack://application:,,,/MDKOSS.UI.WPF;component/Themes/...`，否则从 Sample exe 加载会找不到字典。

---

## 8. 依赖与边界

- Prism.DryIoc 9、`net8.0-windows`、x64。
- 项目引用：`MDKOSS.Core`、`MDKOSS.Extensions`。
- `MdkPlugins.targets` 复制 Drivers / Extensions 到 `plugins/`（默认不含 PNP）。
- **UI.WPF 不引用 Cef / Cef.Extensions。** HMI 画布、HTML 组态不属于本项目。
- 运动 / IO / 视觉算法改 Core 或插件；本项目只做呈现与下发。

## 9. 验证

```bash
dotnet build src/MDKOSS.UI.WPF/MDKOSS.UI.WPF.csproj -c Debug -p:SkipMdkPlugins=true
dotnet run --project src/MDKOSS.UI.WPF/MDKOSS.UI.WPF.csproj -c Debug
```

手测：主界面订单与启停 → 关于进入监控总览 → 切调试 / 配置看主题色带是否切换 → 抽一页危险写确认框 → 配置页应用后未保存不丢编辑、保存后 JSON 有字段。
