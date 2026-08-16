# MDKOSS 学习成本与学习路径

面向要在本仓库上做机型、改配置、写扩展或改内核的人。配套文档见 [README.md](./README.md)。

MDKOSS 是**配置驱动的设备运行时**，不是通用 Web/桌面框架。学习成本主要不在语法，而在：**工业设备分层（驱动 / 设备 / 任务 / 变量 / 配方）+ 配置与代码的边界**。

---

## 1. 框架在学什么

先建立一张图，后面所有路径都围着它转：

```text
setting.json 描述工程
        │
        ▼
   MdkRuntime 按配置实例化
        │
   ┌────┼────┬────────┬────────┐
   ▼    ▼    ▼        ▼        ▼
 Driver Device Task  Vars   Recipe
   │      │    │        │        │
   └──────┴────┴────────┴────────┘
                 │
                 ▼
        HTTP 监控 + CEF HMI
```

**快速开发**指的是：多数机型差异写在 JSON 和少量任务/扩展里，而不是从零搭运动、IO、监控、配方。

| 你通常改什么 | 改哪里 | 要不要写 C# |
|--------------|--------|-------------|
| 换卡、改 IO 点、改轴参数、换配方 | `*.setting.json` 或 Config.Wpf | 否 |
| 新通信外设（串口/TCP/相机…） | `MDKOSS.Extensions.*` | 是，按模板 |
| 新机型工艺（点胶 / 贴片 / PNP） | 宿主 + 任务 + 可选 HMI | 是 |
| 新运动卡 | `MDKOSS.Drivers.*` | 是，契约面大 |
| 改内核生命周期 / 调度 | `MDKOSS.Core` | 是，最后再碰 |

---

## 2. 学习成本总评

相对「从零写运动控制上位机」，MDKOSS 把启动、监控、IO/轴/平台、配方、SQLite 排单做成了现成闭环，**集成与改机型的成本明显低于自研**。

相对「只会 C# 窗体、没碰过运动卡」的人，门槛在**领域模型**，不在语言。

| 维度 | 成本 | 说明 |
|------|------|------|
| 环境与跑通 Demo | 低 | `dotnet run` + 浏览器打开监控页即可；仿真驱动不需要板卡 |
| 读懂配置、改点位/轴/配方 | 中低 | 字段多，但结构固定；Config.Wpf 能降低手写 JSON 出错 |
| 改 HMI / 调 REST | 中 | HTML/JS + 固定页面分层；API 与设备 action 对齐 |
| 做一台新机型（仿 Dispenser） | 中 | 配置 + 一个工艺任务 + 可选机型页；有完整样板 |
| 做一种新扩展设备 | 中 | `extensions.md` 有逐步清单，对照 Serial/Tcp 复制即可 |
| 做一种新板卡驱动 | 高 | `IDriver` 覆盖 IO、单轴、插补；还要懂厂商 SDK |
| 改 Core 编排 / 注册表 | 高 | 启动顺序、注册表优先级、依赖方向不能反 |

**经验值（全职、有 C# 基础）**：

| 角色 | 能独立干活 | 比较稳 |
|------|------------|--------|
| 工艺/调试（只改配置） | 2～4 天 | 1～2 周 |
| HMI / 监控页 | 3～5 天 | 2 周 |
| 机型应用（任务 + 配方 + 页） | 1～2 周 | 3～4 周 |
| 设备扩展 | 3～7 天 | 2 周 |
| 板卡驱动 | 2～4 周 | 1～2 月（含现场） |
| 内核 | 不建议作为入门目标 | 1 月以上 |

没有运动控制背景时，在「配置 / 轴 / GPIO 地址」上再加约 **30%～50%** 时间。

---

## 3. 先备什么

### 3.1 必会（所有写代码的角色）

- C# / .NET 8：类、接口、字典、异步 `StopAsync`、`IDisposable`
- JSON：能对照 `sample.setting.json` 改字段，不靠猜
- 工业常识：DI/DO、轴号、脉冲当量、回零、软限位、配方 ≠ 工单

### 3.2 强烈建议

- REST：`GET /api/status`、`POST /api/devices/{id}/action`
- 一点 HTML/JS：`fetch`、查询参数 `deviceId`
- 插件思路：Core 不引用扩展；启动先 `DiscoverAndRegister()`

### 3.3 按角色再补

| 角色 | 再补 |
|------|------|
| 配置 / 现场 | Config.Wpf；GTS/DMC 点号从 0 还是从 1 |
| HMI | `views/` 前缀约定；`tool_common.js` / `tool_nav.js` |
| 机型 | `MTaskBase`、`MVarStore`、配方键范围 |
| 扩展 | `IMdkExtension`、三个注册表（Device / Action / MonitoringModule） |
| 驱动 | `IDriver`、厂商手册、`ioBitBase` |
| 内核 | 启动顺序、`DeviceActionRegistry` 优先于内置 switch |

不必先学：Nancy、Redis、热插拔、完整 mdkruntime。这些明确不在第一阶段。

---

## 4. 成本从哪来

文档已经把概念拆开了，真正耗时间的是**几条容易踩错的边界**：

1. **配置驱动，但不热替换运行中的驱动/设备**  
   `man_*` 页 PATCH 的是内存里的 `MdkSetting`；`POST /api/config/save` 写回 JSON。要让新驱动/设备生效，需要重启运行时。

2. **gpio 建议整机一个实例**  
   点位写成 `driverId|address|label`。地址格式跟卡有关：GTS 的 `bit.n` 从 1，DMC 从 0，SIM 看 `ioBitBase`。

3. **axis / platform 不写在 `devices[]`**  
   轴在 `axes[]`，平台在 `platforms[]`，平台用 `axis.X` 引用轴 id。旧配置会自动迁，新配置不要混回去。

4. **扩展必须在 `new MdkRuntime` 之前注册**  
   漏注册时，JSON 里的 `serialdev` / `tcpdev` 等会变成未知类型或被跳过。

5. **Core 永不引用扩展或板卡实现**  
   新协议不要改 `BootstrapDevices` 的 switch；新卡不要写进 Core。

6. **配方是 vars 子集，排单/示教只在 SQLite**  
   `recipeVarKeys` 管哪些键能进配方；工单、示教点不进 `setting.json`。

7. **HMI 分层不要混用文件名**  
   `index` / `popup_*` / `monitor_*` / `debug_*` / `man_*` 各管一类用户和写权限。

把这 7 条提前记住，后面读代码会快很多。

---

## 5. 推荐总路径（约 10 步）

按顺序走。不要一上来读 Core 全文或对标 mdkruntime。

### 第 0 步：跑通，建立「可观测」直觉（0.5 天）

环境：Windows x64、.NET 8、CEF 模式需要 VC++ 2019+ 可再发行组件。

```bash
dotnet run --project src/MDKOSS.Cef.Sample/MDKOSS.Cef.Sample.csproj
```

然后打开配置里的 `monitoringPrefix`（Cef.Sample 当前是 `http://127.0.0.1:5081/`）：

- 组态主界面：`/index_hmi.html`（Cef.Sample 的 `startPage`）；订单列表：`/index.html`
- IO：`/monitor_io.html`、`/debug_io.html`
- 快照：`GET /api/status`

对照日志 `logs/yyyyMMdd.log`。目标：确认「JSON → Runtime → HTTP/HMI」这条链是通的。

无界面也可以：

```bash
dotnet run --project src/MDKOSS.Sample.Dispenser/MDKOSS.Sample.Dispenser.csproj -- --console
```

### 第 1 步：读三篇文档，建立分层（0.5～1 天）

1. [architecture.md](./architecture.md) — 分层、启动顺序、数据流  
2. [project-layout.md](./project-layout.md) — 哪个项目干什么  
3. [configuration.md](./configuration.md) — JSON 顶层字段

读完应能回答：

- `Initialize()` 里 Vars → Drivers → Devices → Tasks 的顺序
- 宿主为什么只引用 Core + Extensions
- `drivers` / `devices` / `axes` / `platforms` / `tasks` / `vars` / `recipes` 各管什么

### 第 2 步：对着一份完整 JSON 拆机器（1～2 天）

建议用点胶机，结构比 DieBonder / PNP 小：

- 配置：[src/MDKOSS.Sample.Dispenser/configs/sample.setting.json](../src/MDKOSS.Sample.Dispenser/configs/sample.setting.json)
- 说明：[src/MDKOSS.Sample.Dispenser/README.md](../src/MDKOSS.Sample.Dispenser/README.md)

按这个清单在 JSON 里找对应节点：

| 问题 | 在 JSON 里找 |
|------|----------------|
| 几张卡？仿真还是真卡？ | `drivers[].type`（先看 `sim`） |
| 哪些输入输出？ | `devices` 里 `type=gpio` 的 `in.*` / `out.*` |
| 几根轴、组成什么平台？ | `axes[]` + `platforms[]` 的 `axis.X/Y/Z` |
| 周期在跑什么？ | `tasks[]` 的 `type` |
| 换产品改哪些数？ | `recipeVarKeys` + `recipes[].vars` |
| 打开哪张首页、哪个端口？ | `startPage`、`monitoringPrefix` |

同时用 Config.Wpf 打开同一份配置，对照左侧模块树：

```bash
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj -- --setting src/MDKOSS.Sample.Dispenser/configs/sample.setting.json
```

### 第 3 步：用监控 API 动手改运行态（0.5～1 天）

读 [monitoring-api.md](./monitoring-api.md) 的前半（快照、IO、device action）。

建议自己做一遍：

1. `GET /api/status`，在返回里找到 gpio 的 `gpioIoPoints`、平台的 `platformAxes`
2. `POST /api/io/write` 改一个 `out.*` 别名（仿真即可）
3. `POST /api/devices/{id}/action` 对轴或平台发 `enable` / `move`（先看 debug 页在发什么）
4. 打开 `/debug_platform.html?deviceId=head-dispense`，点动一次，再回头看 action 名称

目标：HMI 不是另一套系统，只是 Runtime 的客户端。

### 第 4 步：读核心子系统，只追 5 个类型（1～2 天）

读 [core-subsystems.md](./core-subsystems.md)，源码只跟这些：

| 类型 | 文件（起点） | 要抓住的一点 |
|------|----------------|--------------|
| `MdkRuntime` | `src/MDKOSS.Core/core/mdk.cs` | 生命周期与 Bootstrap 顺序 |
| `IDriver` | `src/MDKOSS.Core/core/drivers/idriver.cs` | 卡只负责读写与运动原语 |
| `MDeviceBase` | `src/MDKOSS.Core/core/mdev.cs` | 设备把驱动组合成业务对象 |
| `MTaskScheduler` / `MTaskBase` | `src/MDKOSS.Core/core/mtask.cs` | 任务按 `intervalMs` 跑 |
| `MVarStore` | `src/MDKOSS.Core/core/mvar.cs` | 任务、配方、HMI 的共享黑板 |

先不要精读所有 `*ParameterSet`。需要改某类设备时再打开对应解析类。

### 第 5 步：做一次「只改配置」的练习（0.5 天）

在仿真配置上改小东西并重启验证：

- 给 gpio 加一个 `out.tower.blue`（或改已有灯的地址）
- 改某轴 `maxVel` / `pulsePerUnit`
- 复制一条 recipe，改点胶行列或速度，再切换 `activeRecipeId`

过关标准：改完能在监控页或 `/api/status` 里看到，且没有 `MdkException`。

### 第 6 步：按角色分叉

到这里，公共基础已经够用。后面不要全学，选一条主路径（见第 6 节）。

### 第 7 步：对照样板做最小增量（3～10 天，视角色）

| 主路径 | 最小练习 | 样板 |
|--------|----------|------|
| 机型 | 改点胶点阵或加一个安全互锁 | `MDKOSS.Sample.Dispenser` |
| 扩展 | 新 `type` 能创建、能 action、能打到 HTTP | `Extensions.Serial` 或 Sample 里的 `SampleExt` |
| HMI | 新 `monitor_*` 或机型 `indexXxx.html` | `src/MDKOSS.Cef/views/README.md` |
| 驱动 | 在 Sim 上理解 IO/轴语义，再对真卡 | `MDKOSS.Drivers.Sim` |

### 第 8 步：补持久化与「什么不在 JSON 里」（0.5 天）

读 [data-persistence.md](./data-persistence.md)：

- 配方：JSON ↔ SQLite 启动/退出同步
- 排单、示教点：只在库里
- schema v2 配置表：给 Config.Wpf 导出用，不是运行时主路径

### 第 9 步：会查、会验，再碰 Core

- 问题类型 → 改哪里：见 [src/AGENTS.md](../src/AGENTS.md) 的对照表
- 测试：`dotnet test tests/MDKOSS.Tests/MDKOSS.Tests.csproj -c Debug --filter FullyQualifiedName~<类名>`
- 驱动改动先用 `sim` 证明行为，不要默认本机有板卡

---

## 6. 分角色路径

### 6.1 工艺 / 现场 / 配置工程师

**目标**：不写代码也能改工程、联调仿真、切配方。

1. 第 0～3 步 + 第 5 步  
2. [gui.md](./gui.md) + Config.Wpf 的 Drivers / Gpios / Axis / Platform / Recipes  
3. GPIO 地址规则（[configuration.md](./configuration.md) 的 gpio 一节）  
4. 示教：`/debug_platform.html` 与 [data-persistence.md](./data-persistence.md) 示教点  

**先不要**：Extensions 源码、`IDriver` 实现、Flow 解释器。

### 6.2 机型应用开发（最常见的「用框架做项目」）

**目标**：像 Dispenser / DieBonder / PNP 那样交出一台可运行的机型。

标准增量（按这个顺序加，不要一上来写大状态机）：

```text
1. 新宿主或复用 Cef.Sample
2. 一份 setting.json（sim 驱动 + gpio + axes + platform）
3. 内置任务：pollDriver + operation + cycle
4. 一个工艺任务（继承 MTaskBase 或 MotionTask）
5. 配方键
6. 可选：机型 API 模块 + indexXxx.html
7. 需要新设备类型时再做 IMdkExtension
```

阅读顺序：

1. `MDKOSS.Sample.Dispenser`（最小完整机型）  
2. `examples/pnp`（设备 + 任务 + API + 静态页注册齐全）  
3. `MDKOSS.Sample.DieBonder`（更复杂：Tray、视觉、换盘）  
4. [extensions.md](./extensions.md) 第 2、4 节（注册时机与检查清单）

工艺任务习惯：

- 用 `MVarStore` 做命令/阶段/互锁（如 `task.xxx.command`、`task.xxx.phase`）
- IO 只写 gpio **别名**，不要在任务里写死 `di.gpi.bit.n`
- 运动走 `AxisDevice` / `PlatformDevice`，不要在任务里直接打驱动 SDK

### 6.3 设备扩展开发

**目标**：新 `type` 能进配置、能创建、能被 HMI/任务调用。

必读：[extensions.md](./extensions.md) 全文（尤其第 4～6 节）。

对照复制：`MDKOSS.Extensions.Serial` 或 `MDKOSS.Sample/SampleExt`。

检查清单（文档原有，学习时当作作业）：

- `MDeviceBase` 生命周期与 `Dispose`
- `*ParameterSet` 默认值安全
- `IMdkExtension.Id` 唯一；`registration.Device` 的 type 与 JSON 一致
- 需要则注册 Action、MonitoringModule
- 宿主在 `new MdkRuntime` **之前**注册
- **不**给 Core 加对扩展项目的引用

### 6.4 HMI / 监控页

**目标**：加页、改交互，且不破坏分层。

1. [src/MDKOSS.Cef/views/README.md](../src/MDKOSS.Cef/views/README.md)  
2. [monitoring-api.md](./monitoring-api.md)  
3. 公共脚本：`tool_common.js`、`tool_nav.js`、`man_editor.js`  
4. 机型页放在对应宿主 `views/`，用 `startPage` 或 `StaticPageRegistry` 挂上  

命名：小写 + 下划线；`monitor_*` 只读，`debug_*` 可写，`man_*` 改配置。

### 6.5 板卡驱动

**目标**：新 `drivers[].type` 能被 `DriverFactory` 创建，并被 gpio/轴用起来。

1. 先读 `IDriver` 与 `MDKOSS.Drivers.Sim`（内存 DI/DO、轴、插补在 10ms 定时器上模拟）  
2. 再读 Gts / Dmc，看同一套地址和运动原语如何落到厂商 API  
3. 新建 `MDKOSS.Drivers.Xxx`，`registration.Driver("xxx", …)`  
4. 在 `DriverParameterPresets` 补默认 JSON，便于 Config.Wpf「重置模板」  

先把 `TryRead` / `Write`、单轴 `MoveAxisTrap` / `Jog` / `Home` 做对，插补放后面。

### 6.6 内核（最后）

只在确认「这不是扩展或配置能解决的问题」之后再改 Core。

优先读：`MdkRuntime.Initialize/Start/StopAsync`、三类注册表、`RuntimeTaskFactory`、`MonitoringServer` 模块装配。

禁止事项见 [extensions.md](./extensions.md) 第 6 节反模式。

---

## 7. 文档怎么读（避免通读）

[docs/README.md](./README.md) 里的三条阅读线仍然成立，这里按**学习阶段**再排一次：

| 阶段 | 读这些 | 先别读 |
|------|--------|--------|
| 入门 | architecture、project-layout、configuration、本页 | winform-epson-rc-design、issues |
| 联调 / HMI | monitoring-api、gui、Cef views README | 扩展开发第 4 节以后 |
| 做扩展 | extensions、core-subsystems 设备层 | 驱动 SDK、Flow 源码 |
| 做机型 | 上述 + 对应 Sample README + data-persistence | Core 全目录 |
| 改配置 UI | gui、`src/MDKOSS.Config.Wpf/design.md` | 运行时任务实现 |
| 现场缺陷流程 | issues.md（与运行时学习无关，可后置） | — |

历史 WinForms 设计稿只作 UI 参考，不是当前实现入口。

---

## 8. 两周自学课表（机型开发向）

给「会 C#、要在两周内能改点胶/贴片一类项目」的人。每天按 4～6 小时计。

| 天 | 做什么 | 过关 |
|----|--------|------|
| D1 | 跑 Cef.Sample + Dispenser；打开监控页和 `/api/status` | 能指着页面说出驱动/设备/任务从哪来 |
| D2 | 读 architecture + configuration；拆 Dispenser JSON | 能手绘该机型的卡、IO、轴、平台 |
| D3 | Config.Wpf 改点位/轴参数/配方并重启验证 | 改动能在 HMI 或快照里看到 |
| D4 | 读 core-subsystems；跟一遍 `MdkRuntime` 启动 | 能口述 Initialize/Start 顺序 |
| D5 | 跟 `DispenseCycleTask` + 配方键 | 能说出点阵如何从 recipe 生成 |
| D6 | 读 monitoring-api；用 action/IO API 复现 debug 页操作 | 不看页面也能发对请求 |
| D7 | 读 extensions 第 1～2 节 + SampleExt | 能说出注册必须发生在 Runtime 之前 |
| D8～D9 | 小改：新 gpio 别名 + 任务里用它做互锁，或新 recipe | 仿真循环行为按预期变 |
| D10 | 加一张只读 `monitor_*` 或改机型首页一块区域 | 命名和主题不破坏分层 |
| D11～D12 | 选做：仿 Serial 加一个空扩展，或给任务加一个 HTTP 命令 | 配置 `type` 能实例化 |
| D13 | 读 data-persistence；看配方同步与排单不在 JSON | 能区分 recipe / order / teach |
| D14 | 用 AGENTS 对照表定位一个假想缺陷；跑一条相关测试 | 知道下次改代码先搜哪里 |

两周结束后，**还不必**碰 GTS/DMC 真卡或改 `MdkRuntime` 内部。真卡联调作为第三周专题。

---

## 9. 对照：学完应能独立完成的事

**配置级**

- 从 Dispenser/Sample 复制工程，改 `projectName`、端口、`startPage`
- 用 sim 搭「两卡 + 一 gpio + 三轴 + 一平台」
- 增删配方键并在监控里切换

**应用级**

- 写一个 `intervalMs` 任务，读 gpio 别名、写 vars、调平台移动
- 为机型加 `/api/xxx/start|stop` 和一张 `indexXxx.html`
- 需要新设备时走 `IMdkExtension`，不改 Core switch

**排错级**

- 扩展 type 不出现 → 先查是否注册、DLL 是否在 `plugins/`
- IO 写不对 → 先查 `driverId|address` 和该卡的 bit 基址
- 改了 man 页没生效 → 是否只改了内存、是否需要重启
- 平台不动 → 轴 id 是否写在 `axes[]`、平台 `axis.X` 是否引用对

---

## 10. 和「学习成本高」的框架比，哪里省时间

| 自研时通常要做的 | MDKOSS 已提供 | 你还要学的 |
|------------------|---------------|------------|
| 进程生命周期、日志 | `RuntimeHost`、`AppLog` | 入口里先发现插件再创建 Runtime |
| 运动卡抽象 | `IDriver` + Sim/Gts/Dmc 插件 | 地址与轴参数如何写进 JSON |
| IO / 轴 / 平台 | Core 内置设备 | 别名、平台 kind、轴引用 |
| 周期任务 | Scheduler + operation/cycle/motion/flow | 工艺状态机怎么拆 |
| 监控和手调 | HttpListener + 一整套 views | 页面分层和 API 对应关系 |
| 配方 / 排单 / 示教 | RecipeManager + SQLite | 哪些进 JSON、哪些进库 |
| 可选项（串口等） | Extensions 插件 | 注册表，而不是改内核 |

省下的是「造运行时」。花掉的是「学会这套名词和配置约定」。对做设备上位机的人，这笔交换通常划算。

---

## 11. 延伸阅读

- [architecture.md](./architecture.md) — 分层与生命周期  
- [configuration.md](./configuration.md) — JSON 字段与设备类型  
- [core-subsystems.md](./core-subsystems.md) — 驱动 / 设备 / 任务 / 变量 / 配方  
- [extensions.md](./extensions.md) — 如何加设备而不改 Core  
- [monitoring-api.md](./monitoring-api.md) — HMI 与 REST  
- [gui.md](./gui.md) — 宿主与 Config.Wpf  
- [src/AGENTS.md](../src/AGENTS.md) — 改代码时的模块对照  
- 样板机型：`src/MDKOSS.Sample.Dispenser/README.md`、`examples/pnp/README.md`、`src/MDKOSS.Sample.DieBonder/README.md`
