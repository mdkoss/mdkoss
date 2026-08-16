# MDKOSS.Config.Wpf

WPF 离线配置工具：菜单操作 + 左树模块/组件导航 + 中部列表（右键编辑）+ 右侧属性编辑。

支持 **JSON ↔ SQLite** 双向打开与保存。

## 运行

```bash
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj -- --setting configs/sample.setting.json
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj -- --db data/mdk.db
```

或 `run-config-wpf.bat`。

## 文档模式

| 操作 | 行为 |
|------|------|
| 打开 JSON / DB | 自动识别格式，进入对应主文档模式 |
| 保存 | 写回当前打开的文件（JSON→JSON，DB→原 DB） |
| 另存为 JSON / 数据库 | 写出并切换主文档 |
| 导出为 JSON / 数据库 | 写出副本，不切换主文档 |

## 界面

详见 [design.md](./design.md)。

组件联调（独立窗口，说明见 `Debug/*.md`）：

| 菜单 | 文档 |
|------|------|
| 调试 → Driver | [Debug/DriverDebug.md](./Debug/DriverDebug.md) |
| 调试 → Axis | [Debug/AxisDebug.md](./Debug/AxisDebug.md) |
| 调试 → Platform | [Debug/PlatformDebug.md](./Debug/PlatformDebug.md) |
| 调试 → CameraDev | [Debug/CameraDevDebug.md](./Debug/CameraDevDebug.md) |
| 调试 → Task 编辑 | [Debug/TaskDebug.md](./Debug/TaskDebug.md) |
| 调试 → Flow 流程编辑 | [Debug/Flow/FlowEditor.md](./Debug/Flow/FlowEditor.md) |
| 调试 → HMI 主界面组态 | 画布拖放，写回 `hmi.layout.json`（随 setting 保存） |
