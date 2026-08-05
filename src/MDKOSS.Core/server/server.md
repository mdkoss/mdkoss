# `src/server` — HTTP 监控服务

本目录编入 `MDKOSS.Core`，用 `HttpListener` 提供静态 HMI 页面与 REST API。端点清单与架构图见 [docs/monitoring-api.md](../../docs/monitoring-api.md)。

## 目录职责

| 文件 | 职责 |
|------|------|
| `monitoringserver.cs` | 监听、路由分发、静态页 / 静态资源、模块注册 |
| `monitoring_api_module.cs` | API 模块基类：JSON 选项、读写 body、统一成功/错误响应 |
| `monitoring_module_registry.cs` | 扩展程序集注入 API 模块（Serial/TCP 等） |
| `api_*_module.cs` | 各资源前缀的具体处理 |
| `*page.cs` | 启动时加载 `views/*.html` 到内存 |

## 请求处理流程

1. `MonitoringServer` 按模块 `RoutePrefix` **前缀匹配**（更具体的前缀须先注册）
2. 模块 `HandleAsync` 返回 `true` 表示已处理；`false` 则继续尝试后续模块 / 静态页
3. 命中注册的 HTML 路由 → 返回页面
4. 否则尝试从 `views/` 提供静态资源（`/css/*`、`*.js` 等）
5. 未命中 → `404 Not Found`

### 前端样式

| 主题 | 文件 | 适用页面 |
|------|------|----------|
| 主界面 | `views/css/main.css` | `index.html` 及同类 HMI 子界面 |
| 调试 / 监控 | `views/css/debug.css` | `monitor_*` / `debug_*` / `man_*` 等 |

扩展模块经 `MonitoringModuleRegistry.Register` 注册，在构造函数中 `CreateModules` 插入；也可用 `AddModule` 在 `Start()` 前追加。

---

## REST API 开发注意事项（本项目约定）

在本目录新增或修改 API 时，优先遵守下列约定；通用 REST 原则与现有实现冲突时，以现有模块为准。

### 1. 路由与资源设计

- **前缀**：一律 `/api/{resource}`，小写、**无尾部斜杠**（见 `RoutePrefix` 注释）
- **资源用名词**：如 `/api/devices`、`/api/orders`；控制类操作可用子路径 + POST（如 `/api/task/start`、`/api/io/write`）
- **HTTP 方法**：
  - `GET` — 只读，无副作用
  - `POST` — 创建或触发动作（写 IO、设备 action、启停任务）
  - `DELETE` — 删除（如订单）
  - 部分更新若需要，用 `PATCH`（订单等）；避免用 GET 写状态
- **剩余路径**：`remainingPath` 为去掉前缀后的片段（保留前导 `/`）。精确匹配时注意 `""` 与 `"/"` 两种空路径
- **注册顺序**：更长/更具体的前缀放前面，避免被较短前缀抢先匹配后又 `return false` 造成困惑

### 2. 请求与响应格式

- **序列化**：响应用 `SnapshotJsonOptions`（camelCase + 缩进）；请求体反序列化用 `IoWriteJsonOptions`（camelCase、大小写不敏感）
- **Content-Type**：JSON 统一 `application/json; charset=utf-8`；HTML 用 `text/html; charset=utf-8`
- **成功形态**（多数写接口）：
  ```json
  { "success": true, "action": "..." }
  ```
  列表/详情可带业务字段：`devices`、`device`、或直接序列化实体
- **失败形态**：优先走基类 `WriteErrorAsync`：
  ```json
  { "success": false, "error": "missing_action" }
  ```
  `error` 用稳定的蛇形/小写标识符（如 `device_not_found`、`invalid_json`），便于前端分支，不要只返回笼统 `"error"`
- **状态码**：成功默认 200；客户端错误用 400（基类 `WriteErrorAsync`）；资源不存在可设 `404`（见订单模块）；方法不允许时勿静默 200

### 3. 输入校验与错误处理

- 解析 JSON 用 `try/catch JsonException` → `invalid_json`
- 必填字段缺失时返回明确错误码（`missing_action`、`invalid_body` 等），再访问 `Runtime`
- 路径参数做 `Uri.UnescapeDataString`（含中文或编码 ID 时）
- Query 解析可参考 `TaskApiModule` / `OrdersApiModule` 的简易解析；注意 `?` 前缀与空值
- 业务失败从 `Runtime` / `DataStore` 取 `error` 字符串原样或映射后返回，不要吞掉异常后假装成功

### 4. 与运行时的边界

- 模块只通过 `MdkRuntime`（及 `DataStore` / `RecipeManager` / `Vars`）访问业务，**不要**在 API 层直接操作硬件驱动
- 设备控制优先复用 `ExecuteDeviceAction` 或既有专用端点（如 `/api/io/write`），避免同一能力两套语义
- 任务启停等写入变量约定（如 `task.operation.command`），由后台任务消费；API 侧保持幂等语义清晰（重复 POST start 的行为与现有任务实现一致即可）
- 耗时/阻塞 IO（串口读写等）在扩展模块中实现时注意取消令牌与超时，避免拖死 `HttpListener` 处理线程

### 5. 安全与部署（当前版本约束）

- **无鉴权、无 HTTPS**；默认监听 loopback（`monitoringPrefix`，常见 `http://127.0.0.1:5080/`）
- 适合本机 HMI / 工控单机；若绑定非本机地址，须自行加认证、TLS 或反向代理
- 不要在响应或日志中输出密钥、完整配置里的敏感字段
- Windows 上注意 URL 保留与端口占用；勿随意同时注册 `[::1]` 与 `127.0.0.1`（见 `AddListenerPrefixes` 注释）

### 6. 可维护性

- 新模块：继承 `MonitoringApiModule`，实现 `RoutePrefix` + `HandleAsync`，在 `MonitoringServer` 构造函数或 Registry 中注册，并更新 [docs/monitoring-api.md](../../docs/monitoring-api.md)
- 扩展程序集模块：经 `MonitoringModuleRegistry` 注册，勿硬编码进 Core 构造函数列表（除非是 Core 内置资源）
- 静态页：在 `*page.cs` 加载，并在 `_staticPages` 登记路径
- 时间字段优先 UTC（如 `timestampUtc`），与前端约定一致
- 保持与现有 HMI（`src/MDKOSS.Cef/views/*.html`）字段名兼容；破坏性变更需同步改页面与文档

### 7. 常见坑（对照本仓库）

| 坑 | 建议 |
|----|------|
| `RoutePrefix` 带尾部 `/` 或大小写混乱 | 统一小写、无尾斜杠 |
| GET 带写副作用 | 读写分离；写操作用 POST |
| 全部返回 200 + 业务 error | 至少区分 400/404；与 `success` 字段一致 |
| 前缀注册过短抢匹配 | 具体前缀先注册；未处理时 `return false` |
| 列表接口无边界 | 大数据量时考虑过滤/分页（订单已支持 `?status=`） |
| 文档与实现不一致 | 改路由后同步 `monitoring-api.md` 与相关 `views/*.md` |

---

## 新增 API 模块检查清单

1. 新建 `api_xxx_module.cs`，继承 `MonitoringApiModule`
2. 设定 `RoutePrefix`，实现方法分支与校验
3. 复用 `WriteSuccessAsync` / `WriteErrorAsync` / `ReadBodyAsync` / `Deserialize`
4. 在 `MonitoringServer` 或 `MonitoringModuleRegistry` 注册（注意顺序）
5. 更新 `docs/monitoring-api.md`；若 HMI 调用，同步页面与 `src/views` 旁说明
6. 用本机 `monitoringPrefix` 手工或测试项目验证成功/失败路径

## 相关文档

- [docs/monitoring-api.md](../../docs/monitoring-api.md) — 路由一览、静态页、安全说明
- [docs/extensions.md](../../docs/extensions.md) — 扩展如何挂接 API 模块
- [docs/data-persistence.md](../../docs/data-persistence.md) — orders / teach / recipe 持久化
- [src/MDKOSS.Cef/views/index.md](../views/index.md) — 主界面所用接口
