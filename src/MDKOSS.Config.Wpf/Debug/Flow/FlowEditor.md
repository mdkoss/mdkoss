# Flow 流程编辑器说明

> 实现：`FlowEditorWindow.xaml(.cs)`、`FlowEditorVm.cs`  
> 运行时：`MDKOSS.Core.Flow` + `MDKOSS.Tasks.FlowTask`  
> 入口：菜单 **调试 → Flow 流程编辑…**；Tasks 选中 `type=flow` 时右键亦可打开

## 定位

自由节点图编辑 `flow` 任务流程，序列化为 `parameters.flowJson`；运行时由 `FlowTask` 每 tick `Pump` 执行。

## 布局

| 区域 | 内容 |
|------|------|
| 顶栏 | Task 下拉、IntervalMs、校验 / 删除 / 应用到工作区 |
| 左 | 节点工具箱 |
| 中 | Canvas 画布（拖节点；输出端口 → 点击目标节点连线） |
| 右 | 选中节点 Props、输出端口按钮、变量表、函数表 |
| 底 | 校验结果 + flowJson 预览 |

## flowJson 结构

见 Core `FlowDocument`：`version` / `variables` / `functions` / `nodes` / `edges`。

节点 kind：`start` `end` `declareVar` `setVar` `if` `while` `delay` `call` `op.writeIo` `op.deviceAction` `op.log`。

端口：`next` / `true`/`false` / `body`/`exit`。

## 执行语义

- 入口：`functions.main.entryNodeId`（须为 `start`）
- 每 tick 最多 256 步；`delay` 跨 tick Waiting
- 完成后若 `parameters.loop=true`（默认）则 Reset 再跑
- 局部变量镜像到 `task.{name}.flow.var.*`；状态 `task.{name}.flow.state|pc|lastError`

## 修改指引

1. **新节点类型**：`FlowNodeKinds` + 解释器 `Step` + 工具箱按钮 + 默认 Props
2. **表达式**：`FlowExpr`（字面量/变量/比较/逻辑/四则）
3. **UI 连线**：当前为「端口按钮 → 点目标节点」；可改为锚点拖拽
