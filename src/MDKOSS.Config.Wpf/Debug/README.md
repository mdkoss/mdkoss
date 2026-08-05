# 组件调试界面索引

独立于主配置编辑窗；按组件类型各一份实现与说明。

| 界面 | 窗口类 | 说明文档 |
|------|--------|----------|
| Driver | `DriverDebugWindow` | [DriverDebug.md](./DriverDebug.md) |
| Axis | `AxisDebugWindow` | [AxisDebug.md](./AxisDebug.md) |
| Platform | `PlatformDebugWindow` | [PlatformDebug.md](./PlatformDebug.md) |
| CameraDev | `CameraDevDebugWindow` | [CameraDevDebug.md](./CameraDevDebug.md) |
| Task 编辑 | `TaskDebugWindow` | [TaskDebug.md](./TaskDebug.md) |
| Flow 流程 | `Flow/FlowEditorWindow` | [Flow/FlowEditor.md](./Flow/FlowEditor.md) |

共享：`DebugSession.cs`（连接驱动、IO 位表、日志）；Flow 运行时见 Core `MDKOSS.Core.Flow`。

入口：主窗口菜单 **调试**，或中部列表右键 **打开调试界面…**（`type=flow` 打开流程编辑器）。
