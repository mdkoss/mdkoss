# Flow 流程编辑器说明

> 实现：`FlowEditorWindow.xaml(.cs)`、`FlowEditorVm.cs`  
> 运行时：`MDKOSS.Core.Flow` + `MDKOSS.Tasks.FlowTask`  
> 入口：菜单 **调试 → Flow 流程编辑…**；Tasks 选中 `type=flow` 时右键亦可打开

## 定位

参考 C# Workflow（Sequence）风格：节点**自上而下、水平居中**排列，**连接线自动生成**；序列化为 `parameters.flowJson`，运行时由 `FlowTask` 每 tick `Pump` 执行。

## 布局

| 区域 | 内容 |
|------|------|
| 顶栏 | Task、IntervalMs、上移/下移、重新排布、校验、删除、应用到工作区 |
| 左 | 工具箱（点击即插入到选中节点下方） |
| 中 | 纵向居中画布 + 自动箭头连线 |
| 右 | 选中节点 Props、变量表、函数表 |
| 底 | 校验结果 + flowJson 预览 |

## 编辑操作

1. 点工具箱 → 插入到当前选中项之后（默认在 `end` 之前）并自动连线  
2. **上移 / 下移**（Ctrl+Up/Down）调整顺序，自动重连  
3. **重新排布** → 垂直居中 + 重算边  
4. `start` / `end` 为端点，不可删  
5. `if` / `while`：`true`/`false` 或 `body`/`exit` 默认接到后续脊柱节点（绿色/橙色标注）

## flowJson 结构

见 Core `FlowDocument`：`version` / `variables` / `functions` / `nodes` / `edges`。

节点 kind：

- 控制：`start` `end` `declareVar` `setVar` `if` `while` `delay` `call` `op.writeIo` `op.deviceAction` `op.log`
- Motion：`motion.axisMoveTo` `motion.axisEnable` `motion.platformSetMotion` `motion.platformStart` `motion.platformStop` `motion.platformAxisMoveTo` `motion.gpioWrite` `motion.gpioRead` `motion.deviceSnapshot` `motion.ensureDriver` `motion.setParam` `motion.getParam` `motion.setTaskVar` `motion.setGlobalVar`

端口：`next` / `true`/`false` / `body`/`exit`。

## Motion 节点 props

| kind | props |
|------|--------|
| axisMoveTo | deviceId, position(expr) |
| axisEnable | deviceId, enabled(expr) |
| platformSetMotion | deviceId, enabled(expr) |
| platformStart/Stop | deviceId |
| platformAxisMoveTo | deviceId, axis, position(expr) |
| gpioWrite | deviceId, alias, value(expr) |
| gpioRead | deviceId, alias, name(→局部变量) |
| deviceSnapshot | deviceId, prefix(默认 snap → snap.type/state/driverConnected) |
| ensureDriver | deviceId |
| setParam / getParam | key；getParam 另需 name |
| setTaskVar | key(后缀) → `task.{task}.{key}` |
| setGlobalVar | key → 全局 MVar |

## 执行语义

- 入口：`functions.main.entryNodeId`（须为 `start`）
- 每 tick 最多 256 步；`delay` 跨 tick Waiting
- 完成后若 `parameters.loop=true`（默认）则重新进入 `main`（**保留**局部变量）
- 局部变量镜像到 `task.{name}.flow.var.*`；状态 `task.{name}.flow.state|pc|lastError`

## 修改指引

1. **新节点类型**：`FlowNodeKinds` + 解释器 `Step` + 工具箱按钮 + `ApplyDefaultProps`
2. **表达式**：`FlowExpr`
3. **布局**：`FlowEditorVm.RelayoutAndAutoWire`（垂直居中 + 脊柱 `next` 自动连线）
