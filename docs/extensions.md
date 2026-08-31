# 扩展机制（MDKOSS.Extensions）

本文说明 **为何将可选设备拆成独立扩展项目**、**统一接入接口**，以及 **如何按同一模式开发新扩展**。

参考实现：
- `src/MDKOSS.Extensions/`（接入层）
- `src/MDKOSS.Extensions.Camera/`（独立包 `extcamera`）
- `src/MDKOSS.Extensions.PyScript/`（独立包 `devpyscript`）

---

## 1. 设计意义

### 1.1 问题：内核膨胀

串口（`System.IO.Ports`）、TCP 套接字等能力与运动控制内核（驱动 / GPIO / 轴 / 平台 / 任务调度）职责不同：

| 维度 | Core 内核设备 | 通信类扩展设备 |
|------|---------------|----------------|
| 依赖 | `IDriver`、运动卡 / 仿真 | OS 通信栈、第三方协议库 |
| 生命周期 | 随驱动连线、任务调度 | 独立 open/connect，可热插拔式开关 |
| 部署场景 | 几乎每个项目都需要 | 按现场通信需求可选 |

若把串口、TCP 直接写进 `MDKOSS.Core`，会出现：

- Core 被迫引用平台/重量级 NuGet（如 `System.IO.Ports`）
- 不需要通信设备的部署仍携带相关依赖与代码面
- 每加一种协议设备都要改 Core 的 `BootstrapDevices` / `ExecuteDeviceAction` 分支，内核难以稳定

### 1.2 解法：独立程序集 + 注册表解耦

扩展采用 **「Core 定义插槽，Extensions 统一接入，独立 DLL 填实现」**：

```text
MDKOSS.exe
  └─ MdkExtensionHost.Register(...)   ← 启动时注入扩展包
         │
         ▼
MDKOSS.Extensions  ──引用──►  MDKOSS.Core
  (IMdkExtension / Host           (注册表 + 运行时编排)
   )
         ▲
         │ 实现 IMdkExtension
MDKOSS.Extensions.Camera 等独立 DLL
```

**依赖方向单向**：`扩展包 → Extensions → Core`，**Core 永不引用扩展**。  
Extensions 提供统一接入接口 `IMdkExtension` / `IExtensionRegistration` / `MdkExtensionHost`；各扩展在进程启动时通过 facade 写入 Core 注册表。

### 1.3 带来的价值

1. **内核可单独编译与测试** — 不装串口包也能验证驱动、任务、GPIO。
2. **可选能力按需装配** — 主程序 `MdkExtensionHost.Register(...)`；测试或精简宿主可跳过部分包。
3. **扩展点稳定** — 新设备类型实现 `IMdkExtension` 即可，不必改 Core 的 `BootstrapDevices` switch。
4. **与监控/动作体系统一** — 设备工厂、统一 action、HTTP 模块走同一套注册机制。
5. **演进空间** — 可拆出更多扩展 DLL（相机、扫码枪、Modbus 等），遵守 `IMdkExtension` 即可。

### 1.4 边界划分

| 项目 | 包含 | 不包含 |
|------|------|--------|
| **MDKOSS.Core** | gpio / vio / axis / platform / cameradev、驱动、任务、监控内核、SQLite、三类注册表定义 | 串口/TCP 实现、`System.IO.Ports`、扩展接入 facade |
| **MDKOSS.Extensions** | `IMdkExtension` / `MdkExtensionHost` 接入层 | GUI、`Program` 入口 |
| **设备扩展 DLL** | `Extensions.Serial` / `Tcp` / `Camera` 等 | 内核逻辑 |
| **HMI 组态 DLL** | `MDKOSS.Cef.Extensions`（主界面组态宿主 + 内置控件包） | CefSharp、机型流程 |
| **宿主** | `Program`、WinForms/CEF、配置编辑 | 业务设备逻辑本身 |

Core 中的扩展 **插槽**：

| 注册表 | 文件 | 作用 |
|--------|------|------|
| `DeviceExtensionRegistry` | `device_extension_registry.cs` | 按 `type` 字符串创建设备 |
| `DeviceActionRegistry` | `device_action_registry.cs` | 按设备类型匹配统一 action |
| `MonitoringModuleRegistry` | `server/monitoring_module_registry.cs` | 注入 HTTP API 模块 |

枚举侧：`MDeviceType` 可为扩展类型预留值（如 `SerialDev`、`TcpDev`）；**创建设备的分支逻辑不写死在 Core**，而由注册表完成。

---

## 2. 运行时如何接入

### 2.1 启动顺序（必须）

`Program.Main` **第一行**（创建 `MdkRuntime` 之前）：

```csharp
// 扫描 BaseDirectory / plugins / extensions，自动加载 MDKOSS.Drivers.* / MDKOSS.Extensions.* / MDKOSS.Sample.Pnp
MdkExtensionHost.DiscoverAndRegister(new ExtensionDiscoveryOptions
{
    Log = msg => AppLog.Info(msg),
});
```

未注册时：配置里的扩展 `type` 会落入「未知设备类型」或被跳过，对应 HTTP 模块也不会挂载。

### 2.2 统一接入接口

| 类型 | 职责 |
|------|------|
| `IMdkExtension` | 扩展包契约：`Id` + `Register(IExtensionRegistration)` |
| `IExtensionRegistration` | 统一 facade：`Device` / `Action` / `MonitoringModule` / `Task` / `Driver` / `StaticPage` |
| `MdkExtensionHost` | 按 `Id` 幂等注册扩展包 |
| `MdkExtensionHost.DiscoverAndRegister` | 扫描插件目录并自动注册所有 `IMdkExtension` |

### 2.3 设备扩展包注册内容

每个设备扩展（如 `SerialExtension`）在 `Register(IExtensionRegistration)` 中完成：

1. **设备工厂** — `registration.Device("serialdev", …)`
2. **动作处理器** — `registration.Action(predicate, handler)`
3. **监控模块** — `registration.MonitoringModule(runtime => new XxxApiModule(runtime))`

### 2.4 设备创建路径

`MdkRuntime.BootstrapDevices` 顺序大致为：

1. Core 内置：`gpio` / `vio` / `platform*`
2. **`DeviceExtensionRegistry.TryCreate`**（扩展类型落点）
3. 需要 `driverId` 的内置：`axis` / `cameradev`
4. 仍无法识别则抛 `UnsupportedDeviceType`

扩展设备创建成功后，与内置设备相同：`Initialize()` → 加入 `_devices` → `Start()` 时统一启动。

### 2.5 统一动作路径

`MdkRuntime.ExecuteDeviceAction`：

1. 先 `DeviceActionRegistry.TryExecute`（扩展优先）
2. 再 fallback 到 Core 内置 GPIO/VIO/Axis/Platform 逻辑

因此扩展设备可参与监控页「设备动作」与任务侧统一调用，无需改 Core 的 `switch`。

### 2.6 监控 HTTP 路径

`MonitoringServer` 构造时：

```csharp
_modules.AddRange(MonitoringModuleRegistry.CreateModules(runtime));
```

扩展模块按 `RoutePrefix`（如 `/api/serial`）分发；Core 内置模块（status / io / devices / recipe / …）不经过 Extensions。

---

## 3. 现有扩展一览

| 配置 `type` | 设备类 | 参数解析 | 动作 | HTTP | 程序集 |
|-------------|--------|----------|------|------|--------|
| `serialdev` | `SerialDevice` | `SerialDeviceParameterSet` | open/close/write/read/status | `/api/serial/*` | MDKOSS.Extensions.Serial |
| `tcpdev` | `TcpDevice` | `TcpDeviceParameterSet` | connect/disconnect/write/read/status | `/api/tcp/*` | MDKOSS.Extensions.Tcp |
| `mysqldev` | `MysqlDevice` | `MysqlDeviceParameters` | connect/disconnect/ping/query/execute/scalar/status | `/api/mysql/*` | MDKOSS.Extensions.Mysql |
| `extcamera` | `ExtCameraDevice` | `ExtCameraDeviceParameters` | open/close/trigger/startgrab/param/list/status | `/api/extcamera/*` | MDKOSS.Extensions.Camera |
| `devpyscript` | `PyScriptDevice` | `PyScriptDeviceParameters` | run/kill/status | `/api/pyscript/*` | MDKOSS.Extensions.PyScript |
| `devmodserver` | `ModServerDevice` | `ModServerDeviceParameters` | start/stop/status/寄存器读写 | `/api/modserver/*` | MDKOSS.Extensions.ModServer |

分层角色（以串口为例）：

| 文件 | 职责 |
|------|------|
| `serialdev.cs` | 设备本体 + 内部 `SerialDriver` 占位（满足 `MDeviceBase` 对 `IDriver` 的构造要求） |
| `serial_device_parameters.cs` | 从 `DeviceConfig.Parameters` 解析端口参数 |
| `ExtensionDeviceActions.cs` | 统一 action 分发 |
| `SerialDeviceApi` / `TcpDeviceApi` | 供 HTTP 模块调用的运行时 API |
| `api_serial_module.cs` | `MonitoringApiModule` 路由实现 |
| `serialdev.md` | 命令语义对照（OpenCom / Print# 等） |

TCP 侧对称：`tcpdev.cs`、`tcp_device_parameters.cs`、`api_tcp_module.cs`、`tcpdev.md`。
MySQL 侧对称：`mysqldev.cs`、`mysql_device_parameters.cs`、`api_mysql_module.cs`、`mysqldev.md`。

NuGet：扩展项目单独引用 `System.IO.Ports`；Core 无此依赖。

---

## 4. 如何进行扩展开发

以下以新增一种设备类型 `foo`（假设为某协议客户端）为例。可直接对照 `serialdev` / `tcpdev` 复制改造。

### 步骤 0：判断放哪里

| 情况 | 建议 |
|------|------|
| 与串口/TCP 类似的可选通信设备 | 新建 `src/MDKOSS.Extensions.Xxx`，实现 `IMdkExtension` |
| 依赖很重、发布节奏独立 | 新建 DLL（如 `MDKOSS.Extensions.Camera`），引用 Extensions，实现 `IMdkExtension` |
| 强依赖运动驱动、几乎必选 | 优先放 Core（gpio/axis 模式），而不是扩展 |

参考实现：`src/MDKOSS.Extensions.Camera/`（`CameraExtension : IMdkExtension`）。

### 步骤 1：设备类（`MDeviceBase`）

```csharp
public sealed class FooDevice : MDeviceBase
{
    public FooDevice(string id, string name, FooConfig config, MVarStore vars)
        : base(id, name, MDeviceType.Generic /* 或新增枚举值 */, new FooDriverPlaceholder(), vars)
    {
        // ...
    }

    public override void Initialize() { /* 可选 */ base.Initialize(); }
    public override void Start() { /* 扩展设备常覆盖：勿强依赖运动卡 IsConnected */ }
    public override void Stop() { /* 关闭连接 */ }
    public override void Dispose() { /* 释放句柄 */ base.Dispose(); }
    public override DeviceSnapshot GetSnapshot() { /* 监控用 */ }
}
```

注意：

- `MDeviceBase` 构造需要 `IDriver`。通信设备通常提供 **轻量占位驱动**（如 `SerialDriver` / `TcpDriver`），表示「连接状态」由设备自己管理，而不是运动卡。
- 若默认 `Start()` 的 `EnsureConnected()` 不符合通信设备语义，应重写 `Start`/`Stop`（串口/TCP 已如此）。
- 需要新的 `MDeviceType` 时，在 Core 的枚举中增加一项（仅类型标记）；**工厂仍走注册表**。

### 步骤 2：参数解析

从 `Dictionary<string, string> parameters` 解析配置，给出合理默认值：

```csharp
public static class FooDeviceParameterSet
{
    public static FooConfig ParseConfig(Dictionary<string, string> parameters) { ... }
}
```

配置示例（写入 settings JSON）：

```json
{
  "id": "foo1",
  "name": "Foo Client",
  "type": "foo",
  "enabled": true,
  "parameters": {
    "host": "192.168.1.10",
    "port": "4000"
  }
}
```

字段说明同步到 [configuration.md](./configuration.md)。

### 步骤 3：实现 `IMdkExtension` 并注册

```csharp
public sealed class FooExtension : IMdkExtension
{
    public string Id => "foo";
    public string DisplayName => "Foo protocol client";

    public void Register(IExtensionRegistration registration)
    {
        registration.Device("foo", (cfg, name, vars, drivers) =>
        {
            var config = FooDeviceParameterSet.ParseConfig(cfg.Parameters);
            return new FooDevice(cfg.Id, name, config, vars);
        });

        registration.Action(
            device => device is FooDevice,
            (device, action, parameters) => ExecuteFoo((FooDevice)device, action, parameters));

        registration.MonitoringModule(runtime => new FooApiModule(runtime));
    }
}

// 宿主：
MdkExtensionHost.Register(new FooExtension());
```

`drivers` 参数在需要绑定已有驱动时使用；纯通信设备可忽略（串口/TCP/extcamera 用 `_`）。

### 步骤 4：统一动作与 HTTP（可选但推荐）

动作名建议小写：`open` / `close` / `write` / `read` / `status` 等，返回 `DeviceActionResult.Ok(...)` 或 `Fail("reason")`。

HTTP：实现 `MonitoringApiModule`，设置不冲突的 `RoutePrefix`（如 `"/api/foo"`），经 `registration.MonitoringModule` 注入。

### 步骤 5：宿主注册

主程序（或测试 fixture）在创建 runtime **之前**：

```csharp
SerialExtensionBootstrap.Register() /* + Tcp/Camera */;
MdkExtensionHost.Register(new FooExtension());
```

宿主项目需 `ProjectReference` 该扩展 DLL。

### 步骤 6：文档与测试

- 设备说明：扩展目录 `README.md` 或 `foo.md`
- 单元/集成测试：可引用扩展程序集，覆盖参数解析、注册后创建设备、action、API
- 示例配置：宿主 `configs/sample.setting.json` 增加一条 `enabled: false` 的示例即可

### 步骤 7：若扩展需要新驱动类型

设备扩展与驱动扩展是正交的：

```csharp
// 在 IMdkExtension.Register 内：
registration.Driver("foocard", () => new DrvFoo());
```

仅当新硬件抽象属于「板卡驱动」时走 `IDriver`；纯 socket/串口/相机会话不必新增 `DriverFactory` 条目。

---

## 5. 开发检查清单

- [ ] 设备类继承 `MDeviceBase`，生命周期与 `Dispose` 正确释放资源
- [ ] `*ParameterSet` 解析完整，缺省值安全
- [ ] 实现 `IMdkExtension`，经 `MdkExtensionHost.Register` 接入（`Id` 唯一）
- [ ] `registration.Device` 的 type 与 JSON `type` 一致（大小写不敏感）
- [ ] 需要监控动作时已 `registration.Action`
- [ ] 需要 REST 时已 `registration.MonitoringModule`，且 `RoutePrefix` 不冲突
- [ ] 宿主在 `new MdkRuntime` **之前** 调用扩展注册
- [ ] Core **未** 增加对扩展程序集的项目引用
- [ ] 配置 / 监控 API / 扩展 README 已更新
- [ ] 测试覆盖「未注册」与「已注册」两条路径（可选但有价值）

---

## 6. 反模式（避免）

| 反模式 | 后果 |
|--------|------|
| 在 Core 的 `BootstrapDevices` 里 `new SerialDevice` | 破坏依赖方向，重新耦合 |
| 忘记 `Register()` 却配置了扩展 type | 运行时类型不受支持或设备缺失 |
| 扩展直接依赖 WinForms/CEF | 扩展无法被控制台/测试宿主复用 |
| HTTP 模块绕过 `MdkRuntime.TryGetDevice` 自己扫全局状态 | 多实例/测试时行为不一致 |
| 把必选运动设备硬塞进 Extensions | 增加启动仪式与发现成本，收益不大 |

---

## 7. 注册 API 速查

### 统一扩展包（推荐）

```csharp
public interface IMdkExtension
{
    string Id { get; }
    string DisplayName { get; }
    void Register(IExtensionRegistration registration);
}

MdkExtensionHost.Register(IMdkExtension extension);
MdkExtensionHost.RegisterAll(params IMdkExtension[] extensions);

// IExtensionRegistration:
void Device(string deviceType, DeviceFactory factory);
void Action(Func<MDeviceBase, bool> match, DeviceActionHandler execute);
void MonitoringModule(Func<MdkRuntime, MonitoringApiModule> factory);
void Task(string type, Func<TaskBootstrapContext, TaskConfig, string, MTaskBase?> factory);
void Driver(string type, Func<IDriver> factory);
void StaticPage(string path, Func<string> htmlFactory);
```

### 底层注册表（facade 内部转发）

#### 设备工厂

```csharp
DeviceExtensionRegistry.Register(string deviceType, DeviceFactory factory);

// DeviceFactory:
MDeviceBase? (
    MdkSetting.DeviceConfig config,
    string deviceName,
    MVarStore vars,
    IReadOnlyDictionary<string, IDriver> drivers)
```

#### 设备动作

```csharp
DeviceActionRegistry.Register(
    Func<MDeviceBase, bool> match,
    Func<MDeviceBase, string, Dictionary<string, JsonElement>?, DeviceActionResult> execute);
```

#### 监控模块

```csharp
MonitoringModuleRegistry.Register(Func<MdkRuntime, MonitoringApiModule> factory);
```

#### 驱动（可选）

```csharp
DriverFactory.Register(string type, Func<IDriver> factory);

板卡驱动应放在独立项目（如 `MDKOSS.Drivers.Sim`），通过 `IMdkExtension` / `registration.Driver` 接入，勿写回 Core。
```

---

## 8. HMI 控件扩展（不改核心代码）

主界面组态控件由 `MDKOSS.Cef.Extensions` 的 `HmiWidgetRegistry` 载入。内置 `label` / `value` / `lamp` / `progress` / `status` / `button` 与第三方同一路径。

加一种控件（三选一）：

1. 拷文件夹到宿主 `views/widgets/{type}/`（`widget.json` + `widget.js` + 可选 `widget.css`）
2. 随插件放 `plugins/{包名}/widgets/{type}/`
3. DLL 实现 `IHmiWidgetPackage`，或在 `IMdkExtension.Register` 里调用 `HmiWidgetRegistry.Register`（程序集名 `MDKOSS.Cef.Extensions.*.dll` 或 `MDKOSS.Extensions.*.dll`）

运行页通过 `GET /api/hmi/widgets` 拉取目录并动态加载 `script` / `css`。不必改 `hmi_runtime.js`。详见 `src/MDKOSS.Cef.Extensions/README.md`。

---

## 9. 延伸阅读

- [architecture.md](./architecture.md) — 总体分层与启动时序
- [project-layout.md](./project-layout.md) — 解决方案与目录职责
- [configuration.md](./configuration.md) — `serialdev` / `tcpdev` / `extcamera` 参数字段
- [monitoring-api.md](./monitoring-api.md) — `/api/serial/*`、`/api/tcp/*`、`/api/extcamera/*`
- [core-subsystems.md](./core-subsystems.md) — 设备层与调度
- `src/MDKOSS.Extensions.Serial/serialdev.md`、`src/MDKOSS.Extensions.Tcp/tcpdev.md` — 设备命令语义
- `src/MDKOSS.Cef.Extensions/README.md` — HMI 控件扩展（文件夹包 / `IHmiWidgetPackage`）
- `src/MDKOSS.Extensions.Camera/README.md` — 独立扩展包示例
