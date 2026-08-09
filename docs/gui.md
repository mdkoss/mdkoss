# 桌面壳与配置管理 UI

MDKOSS 将桌面宿主拆成独立可执行项目，CEF 仅为界面库，共享 `MdkRuntime` 与 HTTP 监控服务。

## 独立启动项目

| 项目 | 路径 | 入口 | 说明 |
|------|------|------|------|
| **MDKOSS.Config.Wpf** | `src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj` | `App.xaml` → `MainWindow` | WPF 离线配置工具 |
| **MDKOSS.Sample** | `src/MDKOSS.Sample/MDKOSS.Sample.csproj` | `MDKOSS.Sample/Program.cs` → `CefMainForm` | Demo / PNP 宿主；嵌入 CEF HMI；支持 `--console` |
| **MDKOSS.Cef** | `src/MDKOSS.Cef/MDKOSS.Cef.csproj` | `CefMainForm` / `CefRuntimeBootstrap` | CefSharp 界面库 + `views/*.html`（非可执行） |

共用启动逻辑在 `src/MDKOSS.Core/host/RuntimeHost.cs`（配置路径解析、Load / Initialize / Start / Stop）。

```bash
# 配置界面（WPF）
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj -- --setting configs/sample.setting.json

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

## WPF 配置界面（Config.Wpf）

离线配置主界面：左侧模块树、中部组件列表、右侧属性编辑。详见 [MDKOSS.Config.Wpf/design.md](../src/MDKOSS.Config.Wpf/design.md)。

能力概览：

- Drivers / Devices / Axis / Platform / Gpios / Vios / Tasks / Vars / Recipes / SysConfig / Database
- 参数 Key/Value 表编辑、模板补全、Excel 导入导出（Gpios/Vios/Axis/Platform）
- 调试窗：Driver / Axis / Platform / CameraDev / Task / Flow / Vision
- JSON / SQLite 打开、保存、另存、模块导入导出

历史 UI 参考仍见 [winform-epson-rc-design.md](./winform-epson-rc-design.md)。

## CEF 桌面壳

- `CefRuntimeBootstrap` 初始化 CefSharp 运行时
- `CefMainForm` 加载监控 HTTP 前缀下的首页（由配置 `startPage` 指定，缺省 `index.html`）
- 页面内链接跳转至各监控子页（IO、串口、平台示教等）
- 需 VC++ 2019+ 可再发行组件（见 readme 环境说明）

CEF 与 `--console` 共用同一 HTTP 监控端口。

## 与 Web HMI 的关系

```text
Config.Wpf / CEF ──► MdkRuntime ◄── HttpListener (MonitoringServer)
                            ▲
浏览器 views/*.html ────────┘  (REST + 静态页)
```

- **配置编辑**：WPF（`MDKOSS.Config.Wpf`，JSON / SQLite）
- **在线监控**：CEF / 浏览器 HMI
- **设计原则**：离线配置编辑与在线监控分离

## 日志

- `AppLog`（NLog）→ `logs/yyyyMMdd.log`
