# 桌面壳与配置管理 UI

MDKOSS 提供三种宿主模式，共享同一 `MdkRuntime` 实例与 HTTP 监控服务。

## UI 模式

| 模式 | 参数 | 入口类 | 说明 |
|------|------|--------|------|
| WinForms | 默认 / `--winform` | `MainForm` | 原生监控窗 + 配置工具 |
| CEF | `--cef` | `CefMainForm` | Chromium 嵌入 `views/index.html` |
| Console | `--console` | `Program.RunConsoleRuntimeAsync` | 无 GUI，Ctrl+C 停止 |

桌面模式流程（`Program.cs`）：

1. `ExtensionsBootstrap.Register()`
2. `MdkSetting.Load` 默认配置路径
3. `new MdkRuntime(setting)` → `Initialize()` → `Start()`
4. 显示 WinForms 或 CEF 窗体
5. 窗体关闭后 `StopAsync()` + `Dispose()`

Console 模式同样加载配置并启动运行时，仅不创建窗体。

## WinForms 主界面（MainForm）

职责：

- 展示运行状态、项目名、监控 URL
- 打开 **ComponentConfigForm**（统一配置管理）
- 运行时工具：**Device Manager**、**Task Manager**、**I/O Monitor**
- **Diagnostics** 导出（setting、快照、日志）
- 底部 **运行时历史/事件** 面板

监控数据来自 `MdkRuntime.GetSnapshot()` 与 HTTP API 轮询，与浏览器 HMI 一致。

## 配置管理（ComponentConfigForm）

EPSON RC+ 风格的工程配置界面，详见 [winform-epson-rc-design.md](./winform-epson-rc-design.md)。

已实现能力概览：

### 导航结构

- 左侧 **配置树**：Project、Runtime、Drivers、Devices、I/O、Tasks、Variables、Import/Export
- 右侧 **详情页**：网格/表单编辑
- 底部 **状态栏**：配置文件路径、行数统计

### 导入导出

- 整份 setting JSON 导入/导出
- 子系统级导入/导出（如 I/O Labels 单独导出）
- 项目 **备份/恢复** 与保存前 **校验**

### I/O 一等公民

- I/O Labels 页：alias、方向、驱动、地址、描述
- GPIO/VIO parameters 结构化行编辑
- 与现有 JSON `parameters` 形状兼容

### 类型化参数编辑器

- 驱动类型、设备类型（GPIO/VIO/Axis/Platform/Serial/TCP）、任务类型专属表单
- 保留 **原始 parameters** 列作为高级回退
- **参数预设** 模板填充

### 遗留入口

迁移期间保留 GPIO/Axis/Platform/Devs/Tasks 独立按钮，指向各 `*ConfigForm`。

## 运行时管理窗口

| 窗口 | 类 | 功能 |
|------|-----|------|
| Device Manager | `DeviceManagerForm` | 设备状态、驱动连接、enabled、最近错误 |
| Task Manager | `TaskManagerForm` | 任务名、类型、间隔、状态、暂停/恢复/停止 |
| I/O Monitor | `IoMonitorForm` | 输入输出、标签、描述、 live 刷新、安全手动 toggle |

## CEF 桌面壳

- `CefRuntimeBootstrap` 初始化 CefSharp 运行时
- `CefMainForm` 加载输出目录下 `views/index.html`
- 页面内链接跳转至各监控子页（IO、串口、平台示教等）
- 需 VC++ 2019+ 可再发行组件（见 readme 环境说明）

CEF 模式下运行时仍在 `Program` 中启动，浏览器页与 `--console` 共用同一 HTTP 端口。

## 与 Web HMI 的关系

```text
WinForms/CEF ──► MdkRuntime ◄── HttpListener (MonitoringServer)
                      ▲
浏览器 views/*.html ──┘  (REST + 静态页)
```

- **配置编辑**： primarily WinForms（JSON 文件读写）
- **在线监控**： WinForms 工具窗 + 浏览器 HMI 任选
- **设计原则**：离线 JSON 编辑与在线监控分离，见 [winform-epson-rc-design.md](./winform-epson-rc-design.md)

## 日志

- `AppLog`（NLog）→ `logs/yyyyMMdd.log`
- Debug 构建启动时清空当日日志文件
- Diagnostics 导出可打包日志

## 延伸阅读

- [winform-epson-rc-design.md](./winform-epson-rc-design.md) — UI 设计原则与阶段任务清单
- [architecture.md](./architecture.md) — UI 模式与生命周期
- [monitoring-api.md](./monitoring-api.md) — 监控页与 API 对应关系
