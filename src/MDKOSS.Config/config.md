# MDKOSS.Config

WinForms 配置 / 监控可执行项目。入口 `Program.cs` → `RuntimeHost` → `MainForm`。

## 文档

| 文档 | 说明 |
|------|------|
| [design.md](./design.md) | **配置界面设计指引**（布局、原则、窗体职责、校验与变更清单） |
| [docs/gui.md](../../docs/gui.md) | 桌面壳总览（Config + Cef） |
| [docs/winform-epson-rc-design.md](../../docs/winform-epson-rc-design.md) | EPSON RC+ 风格历史设计参考与阶段任务 |
| [docs/configuration.md](../../docs/configuration.md) | setting JSON 字段说明 |

## 运行

```bash
dotnet run --project src/MDKOSS.Config/MDKOSS.Config.csproj
dotnet run --project src/MDKOSS.Config/MDKOSS.Config.csproj -- --setting configs/sample.setting.json
```

WPF 配置工具（JSON + 导出 SQLite）：

```bash
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj
```

见 [MDKOSS.Config.Wpf/README.md](../MDKOSS.Config.Wpf/README.md)。

## 主要窗体

- `MainForm` — 在线五区壳（菜单 / 树 / 表格|结构图 / 属性 / 状态）
- `ComponentConfigForm` — 离线五区配置管理（同骨架）
- `WorkspaceShell` / `StructureDiagramPanel` — 共用布局与关系图
- `DeviceManagerForm` / `TaskManagerForm` / `IoMonitorForm` — 可选浮动巡检窗
