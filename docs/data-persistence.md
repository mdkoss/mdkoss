# 数据持久化（SQLite）

MDKOSS 使用 **Microsoft.Data.Sqlite** 持久化配方同步、生产排单、平台示教点，以及（schema v2）完整工程配置导出表。运行时通过 `MdkDataStore` 访问业务数据；配置工具通过 `MdkConfigStore` 将 `*.setting.json` 导出到规范化表。

## 数据库位置

- 配置项：`MdkSetting.databasePath`
- 默认：`data/mdk.db`（相对 `AppContext.BaseDirectory`）
- 常量：`MdkSetting.DefaultDatabasePath`

`MdkRuntime` 构造时：`DataStore = new MdkDataStore(ResolveDatabasePath(setting))`

## 核心类型

| 类型 | 文件 | 职责 |
|------|------|------|
| `MdkDatabase` | `mdk_database.cs` | 连接、迁移、原始 SQL 执行（当前 schema version = 2） |
| `MdkDataStore` | `mdk_data_store.cs` | 业务 API：订单、配方、示教点 |
| `MdkConfigStore` | `mdk_config_store.cs` | setting JSON ↔ 配置表导出/导入 |
| `MdkDataModels` | `mdk_data_models.cs` | 记录 DTO |

## 配置导出表（schema v2）

| 表 | 来源 | 说明 |
|----|------|------|
| `drivers` | `setting.drivers` | 驱动 id/type/enabled/parameters |
| `devices` | `setting.devices` | 全部设备（含 gpio/axis/platform 等） |
| `gpios` | gpio/vio 的 `in.*`/`out.*` | 规范化点位路由 |
| `axis` | type=`axis` 设备 | 单轴设备投影 |
| `platform` | platform / xy… 族 | 平台设备 + kind |
| `positions` | `teach_points` 镜像 | 导出时从示教点复制 |
| `sysconfigs` | 顶层字段 | projectName/cycleMs/vars/tasks/… |
| `recipes` | `setting.recipes` | 与运行时配方表共用 |
| `logs` | 导出/导入事件 | 审计日志 |
| `langs` | 内置种子 | zh-CN / en-US UI 文案 |

WPF 配置工具：`dotnet run --project src/MDKOSS.Config.Wpf` → 菜单「导出到 SQLite」。

## 生命周期中的数据流

```mermaid
sequenceDiagram
    participant R as MdkRuntime
    participant DS as MdkDataStore
    participant SET as MdkSetting
    participant VS as MVarStore

    R->>DS: BootstrapDatabase()
    DS->>SET: SyncRecipesWithSetting
    DS->>VS: order.list from ListOrders()
    Note over R: ... 运行 ...
    R->>DS: Dispose → PersistRecipesFromSetting
```

### 启动（BootstrapDatabase）

1. 记录数据库路径日志
2. `SyncRecipesWithSetting(Setting)` — JSON recipes 与 DB 双向对齐
3. `ListOrders()` — 若有排单，序列化写入 vars 键 `order.list`（`MdkDataStore.OrderListVarKey`）

### 关闭（Dispose）

- `PersistRecipesFromSetting(Setting)` — 将内存中配方写回 SQLite
- 关闭数据库连接

## 生产排单（production_orders）

`OrdersApiModule` 暴露 REST CRUD：

- 字段包括：产品、数量、状态、进度、关联 `recipe_id`、优先级、备注、时间戳
- 列表默认按优先级降序、创建时间升序
- 支持按 `status` 过滤

排单数据 **不** 嵌入 `setting.json`，仅存在于 SQLite，通过 API 与 vars 缓存暴露给 UI。

## 示教点（teach）

`TeachApiModule` 管理平台示教点文件与点位：

- 按 `platformId` 组织
- 支持列出文件、读写/删除单个示教点
- 供 `monitorPlatform.html` 与 WinForms 示教功能使用

具体表结构见 `MdkDatabase` 迁移脚本。

## 配方同步

配方在 JSON 与 DB 间同步，保证：

- 配置文件中定义的 `recipes` 可导入数据库
- 运行时通过 API 修改的配方在退出时写回 DB
- `MdkRecipeManager` 操作的 vars 与 `RecipeConfig` 一致

启动时若 `activeRecipeId` 无效，记录警告但不阻止启动。

## 与变量中心的关系

| 键 | 来源 | 用途 |
|----|------|------|
| `order.list` | DataStore 启动加载 | 监控/UI 展示排单摘要 |
| `recipe.activeId` / `recipe.activeName` | RecipeManager | 当前配方标识 |

## 测试

`tests/MDKOSS.Tests/Core/core/MdkRecipeTests.cs`、`Core/server/RecipeApiModuleTests.cs` 等用例覆盖配方与 API 行为；测试输出目录下可生成独立 `data/mdk.db`。

## 延伸阅读

- [configuration.md](./configuration.md) — recipes / activeRecipeId 字段
- [core-subsystems.md](./core-subsystems.md) — MdkRecipeManager
- [monitoring-api.md](./monitoring-api.md) — `/api/orders`、`/api/teach`、`/api/recipes`
