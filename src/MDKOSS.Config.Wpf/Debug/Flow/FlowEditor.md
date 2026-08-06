# Flow 流程编辑器说明

> 实现：`FlowEditorWindow.xaml(.cs)`、`FlowEditorVm.cs`  
> 运行时：`MDKOSS.Core.Flow` + `MDKOSS.Tasks.FlowTask`  
> 入口：菜单 **调试 → Flow 流程编辑…**；Tasks 选中 `type=flow` 时右键亦可打开

## 定位

**复合块（Composite）工作流**：根序列自上而下；`if` / `while` 为容器，子节点挂 `parentId` + `slot`（`then`/`else`/`body`）。  
编辑权威是树；`edges` 由 `FlowComposite.BuildEdges` 生成，供解释器执行。

## 布局

| 区域 | 内容 |
|------|------|
| 顶栏 | Task、IntervalMs、上移/下移、重新排布、校验、删除、应用 |
| 左 | 工具箱（插入到**当前插槽**） |
| 中 | 画布：根脊柱 + THEN/ELSE/BODY 区域框；点区域切换插入插槽 |
| 右 | 插槽焦点、Then/Else/Body/父级/折叠、Props、变量、函数 |
| 底 | 校验 + flowJson |

## 复合块编辑

1. 插入 **if** → 自动进入 **Then** 插槽；点 **Else** 再插入假分支  
2. 插入 **while** → 自动生成 Body 占位 `delay(0)`，焦点在 **Body**  
3. 选中子节点 → 插入焦点跟到其所在插槽  
4. **折叠/展开**：隐藏子树区域（节点仍在文档中）  
5. 删除复合块 → **整棵子树**一并删除  
6. 上移/下移：仅在同一 `parentId+slot` 兄弟间移动  

## flowJson

- `nodes[].parentId` / `slot` / `order`（二期）  
- `edges`：可由树生成；旧文档仅有边、无 parentId 时编辑器会把脊柱提升为根序列  

节点 kind：控制 + Motion（见工具箱）。端口：`next` / `true`/`false` / `body`/`exit`。

## 执行语义

- 入口：`functions.main.entryNodeId`（须为 `start`）  
- 每 tick 最多 256 步；`delay` 跨 tick Waiting  
- `loop=true` 时完成后重入 `main`（保留局部变量）  
- 状态：`task.{name}.flow.state|pc|lastError`  

## 修改指引

1. 新节点：`FlowNodeKinds` + 解释器 + 工具箱 + `ApplyDefaultProps`  
2. 复合结构：`FlowComposite`（`BuildEdges` / `ValidateTree`）  
3. 布局：`FlowEditorVm.LayoutSequence` + `Regions`
