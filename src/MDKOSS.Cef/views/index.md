# MDKOSS 主界面（index.html）

面向 `MdkRuntime` 的 HMI 主界面，由 `MonitoringServer` 在 `/` 与 `/index.html` 提供。

## 布局

| 区域 | 内容 |
|------|------|
| 顶部控制栏（居中） | 图标、设备名称、组件 / 任务 / 变量 / 报警 / 工单 / 用户 / 关于；扩展已加载时显示「组态」进入 `index_hmi.html` |
| 主内容区 | 订单列表（可点击行选中当前订单；双击打开工单详情 popup） |
| 底部状态栏 | 当前订单摘要、重要状态监控、**右下角当前排单与选择按钮**、启动 / 停止 / 复位 |

右上角 **报警灯** 同步 `task.operation.lamp`（红 / 黄 / 绿）。

## 数据接口

- `GET /api/status` — 项目名、设备、变量、运行状态（1s 轮询）
- `GET /api/recipe` — 排单（配方）列表与当前激活项
- `POST /api/recipe/apply?id={recipeId}` — 切换当前排单
- `POST /api/task/start|stop|reset` — 写入 `task.operation.command`，由 `task-operation` 任务消费
- `POST /api/task/lamp?color=red|yellow|green` — 三色灯

## 订单变量约定（可选）

- `order.list` 或 `order.list.json`：JSON 数组 `[{ id, product, qty, status, progress, updatedAt }]`
- 或单条：`order.current.id`、`order.current.product`、`order.current.qty` 等
- 未配置时界面显示演示订单，便于联调布局

## 排单（配方）约定

- 配置见 `setting.json` 的 `recipes` / `activeRecipeId` / `recipeVarKeys`
- 运行时变量：`recipe.activeId`、`recipe.activeName`（由 `MdkRecipeManager` 维护）
- 主界面右下角显示当前排单名称，点击「选择排单」打开 `popup_recipe.html`

## 相关文件

- `src/MDKOSS.Cef/views/index.html` — 页面实现
- `src/MDKOSS.Cef/views/popup_*.html` — 二级弹窗
- `src/MDKOSS.Cef/views/css/main.css` — 主界面风格（HMI）
- `src/MDKOSS.Cef/views/css/debug.css` — 调试 / 监控工具页风格
- `src/MDKOSS.Cef/views/README.md` — 界面分组总览
- `src/MDKOSS.Core/server/indexpage.cs` — 嵌入/加载 HTML
- `src/MDKOSS.Core/server/monitoringserver.cs` — 路由、静态资源与任务 API
- 详细监控页：`/monitor_runtime.html`
- 监控组态：`/index_hmi.html`（编辑 `/man_hmi.html`），见 `MDKOSS.Cef.Extensions`
