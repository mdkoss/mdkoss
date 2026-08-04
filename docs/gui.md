# 桌面壳与配置管理 UI

MDKOSS 将桌面宿主拆成独立可执行项目，CEF 仅为界面库，共享 `MdkRuntime` 与 HTTP 监控服务。

## 独立启动项目

| 项目 | 路径 | 入口 | 说明 |
|------|------|------|------|
| **MDKOSS.Config** | `src/MDKOSS.Config/MDKOSS.Config.csproj` | `MDKOSS.Config/Program.cs` → `MainForm` | WinForms 配置 / 监控工具 |
| **MDKOSS.Sample** | `src/MDKOSS.Sample/MDKOSS.Sample.csproj` | `MDKOSS.Sample/Program.cs` → `CefMainForm` | Demo / PNP 宿主；嵌入 CEF HMI；支持 `--console` |
| **MDKOSS.Cef** | `src/MDKOSS.Cef/MDKOSS.Cef.csproj` | `CefMainForm` / `CefRuntimeBootstrap` | CefSharp 界面库 + `views/*.html`（非可执行） |

共用启动逻辑在 `src/MDKOSS.Core/host/RuntimeHost.cs`（配置路径解析、Load / Initialize / Start / Stop）。

```bash
# 配置界面
dotnet run --project src/MDKOSS.Config/MDKOSS.Config.csproj
dotnet run --project src/MDKOSS.Config/MDKOSS.Config.csproj -- --setting configs/sample.setting.json

# Sample + CEF HMI
dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj
dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj -- --setting configs/pnp.setting.json

# 无 GUI 控制台
dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj -- --console --setting configs/pnp.setting.json
```

桌面模式流程：

1. `MdkExtensionHost.DiscoverAndRegister()`（扫描 `plugins/` 自动注册驱动与设备扩展）
2. `RuntimeHost` 解析 `--setting` 并 `MdkSetting.Load`
3. `new MdkRuntime(setting)` → `Initialize()` → `Start()`
4. 显示对应窗体（或 console 阻塞等待）
5. 退出后 `StopAsync()` + `Dispose()`

## WinForms 主界面（MainForm）

五区骨架（与 Config Manager 一致）：顶部菜单、中左资源树、中部表格/结构图、中右属性页、底部状态栏。详见 [MDKOSS.Config/design.md](../src/MDKOSS.Config/design.md)。

职责：

- 展示运行状态、项目名、监控相关信息（状态栏）
- 打开 **ComponentConfigForm**（统一离线配置管理）
- 树节点切换 Drivers / Devices / Tasks / I/O / Variables / History
- **Diagnostics** 导出（setting、快照、日志）
- Tools 菜单仍可打开独立 Device / Task / I/O 浮动窗

监控数据来自 `MdkRuntime.GetSnapshot()` 与任务快照，与浏览器 HMI 一致。

## 配置管理（ComponentConfigForm）

EPSON RC+ 风格的工程配置界面，实现约定见 [MDKOSS.Config/design.md](../src/MDKOSS.Config/design.md)，历史参考见 [winform-epson-rc-design.md](./winform-epson-rc-design.md)。

已实现能力概览：

### 布局

- 顶部 **MenuStrip**：File / Edit / View
- 中左 **配置树**：Project、Drivers、Devices、I/O Labels、Tasks、Variables、Recipes、Import/Export
- 中部 **表格或框架结构图**：概览列；View 菜单切换
- 中右 **PropertyGrid**：选中行详细字段与 parameters
- 底部 **状态栏**：路径、Offline 模式、选中项、计数

### 导入导出

- 整份 setting JSON 导入/导出
- 子系统级导入/导出（如 I/O Labels 单独导出）
- 项目 **备份/恢复** 与保存前 **校验**

### I/O 一等公民

- I/O Labels 页：alias、方向、驱动、地址、描述（描述在属性页）
- GPIO/VIO parameters 结构化行编辑
- 与现有 JSON `parameters` 形状兼容

### 类型化参数编辑器

- 属性页按类型编辑 + **Param Preset** 模板填充
- 保留原始 parameters 文本作为高级回退

### 遗留入口

迁移期间保留 GPIO/Axis/Platform/Devs/Tasks 独立窗体类，主流程不再依赖它们。

## 运行时管理窗口

| 窗口 | 类 | 功能 |
|------|-----|------|
| Device Manager | `DeviceManagerForm` | 设备状态、驱动连接、enabled、最近错误 |
| Task Manager | `TaskManagerForm` | 任务名、类型、间隔、状态、暂停/恢复/停止 |
| I/O Monitor | `IoMonitorForm` | 输入输出、标签、描述、 live 刷新、安全手动 toggle |

## CEF 桌面壳

- `CefRuntimeBootstrap` 初始化 CefSharp 运行时
- `CefMainForm` 加载监控 HTTP 前缀下的首页（PNP 为 `indexPnp.html`，否则 `index.html`）
- 页面内链接跳转至各监控子页（IO、串口、平台示教等）
- 需 VC++ 2019+ 可再发行组件（见 readme 环境说明）

CEF 与 `--console` 共用同一 HTTP 监控端口。

## 与 Web HMI 的关系

```text
Config/CEF ──► MdkRuntime ◄── HttpListener (MonitoringServer)
                      ▲
浏览器 views/*.html ──┘  (REST + 静态页)
```

- **配置编辑**： primarily WinForms（`MDKOSS.Config`，JSON 文件读写）
- **在线监控**： WinForms 工具窗 + CEF / 浏览器 HMI 任选
- **设计原则**：离线 JSON 编辑与在线监控分离，见 [winform-epson-rc-design.md](./winform-epson-rc-design.md)

## 日志

- `AppLog`（NLog）→ `logs/yyyyMMdd.log`
