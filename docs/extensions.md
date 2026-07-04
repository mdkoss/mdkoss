# 扩展机制（MDKOSS.Extensions）

Core 保持最小内核；可选设备与 API 通过 **独立程序集 + 注册表** 接入，避免 Core 依赖 `System.IO.Ports` 等重量级或平台特定包。

## 项目边界

| 项目 | 包含 | 不包含 |
|------|------|--------|
| MDKOSS.Core | gpio/vio/axis/platform、监控内核、SQLite、内置驱动 | 串口/TCP 设备实现 |
| MDKOSS.Extensions | `serialdev`、`tcpdev`、对应 API 模块 | GUI、Program 入口 |
| MDKOSS | Program、WinForms、CEF | 业务设备逻辑 |

Core 中仍保留扩展 **注册表** 定义：

- `DeviceExtensionRegistry`
- `DeviceActionRegistry`
- `MonitoringModuleRegistry`

Extensions 在启动时向这些注册表写入实现。

## 启动注册

`Program.Main` 第一行：

```csharp
ExtensionsBootstrap.Register();
```

必须在创建 `MdkRuntime` **之前** 调用。

### ExtensionsBootstrap 做了什么

`extensions/ExtensionsBootstrap.cs`：

1. **设备工厂** — `DeviceExtensionRegistry.Register("serialdev", ...)` / `"tcpdev"`
2. **动作处理器** — `DeviceActionRegistry.Register` 匹配 `SerialDevice` / `TcpDevice`
3. **HTTP 模块** — `MonitoringModuleRegistry.Register` → `SerialApiModule` / `TcpApiModule`

## 设备扩展

### 注册签名

```csharp
DeviceExtensionRegistry.Register(string deviceType, DeviceFactory factory);

// DeviceFactory:
MDeviceBase? (DeviceConfig config, string deviceName, MVarStore vars, IReadOnlyDictionary<string, IDriver> drivers)
```

### 现有实现

| type | 类 | 参数解析 |
|------|-----|----------|
| `serialdev` | `SerialDevice` | `SerialDeviceParameterSet` |
| `tcpdev` | `TcpDevice` | `TcpDeviceParameterSet` |

`MdkRuntime.BootstrapDevices` 在 Core 内置类型匹配失败后调用 `TryCreate`；成功则与普通设备一样 `Initialize` 并加入 `_devices`。

## 设备动作扩展

```csharp
DeviceActionRegistry.Register(
    Func<MDeviceBase, bool> predicate,
    Func<MDeviceBase, string, Dictionary<string, JsonElement>?, DeviceActionResult> handler);
```

`ExecuteDeviceAction` 优先查注册表，再 fallback 到 Core 内置 GPIO/VIO/Axis/Platform 逻辑。

Serial/TCP 的 open、write、read 等 action 在 `ExtensionDeviceActions` 中实现。

## 监控 API 扩展

```csharp
MonitoringModuleRegistry.Register(runtime => new SerialApiModule(runtime));
```

`MonitoringServer` 构造时：

```csharp
_modules.AddRange(MonitoringModuleRegistry.CreateModules(runtime));
```

模块按 `RoutePrefix` 分发请求，继承 `MonitoringApiModule` 基类。

Core 内置模块（不经过 Extensions）：

- `StatusApiModule` — `/api/status`
- `IoApiModule` — `/api/io/*`
- `DevicesApiModule` — `/api/devices/*`
- `RecipeApiModule`、`OrdersApiModule`、`TeachApiModule`、`TaskApiModule`

## 添加新扩展类型（步骤）

1. 在 `MDKOSS.Extensions`（或新 DLL）中实现 `MDeviceBase` 子类
2. 实现 `*ParameterSet` 解析 `DeviceConfig.Parameters`
3. 在 Bootstrap 中 `DeviceExtensionRegistry.Register`
4. 如需 HTTP：实现 `MonitoringApiModule` 并 `MonitoringModuleRegistry.Register`
5. 如需自定义 action：`DeviceActionRegistry.Register`
6. 在 `sample.setting.json` 增加示例条目
7. 补充 `docs/` 与单元测试

若扩展需 **新驱动类型**，还需 `DriverFactory.Register`（可在 Extensions Bootstrap 或 Core 静态构造函数中完成）。

## 依赖方向

```text
MDKOSS.exe → MDKOSS.Extensions → MDKOSS.Core
                ↑
         ExtensionsBootstrap.Register()
```

Core **不得** 引用 Extensions，保证内核可单独编译与测试（测试项目可引用 Extensions 以覆盖 serial/tcp）。

## 延伸阅读

- [configuration.md](./configuration.md) — serialdev / tcpdev 参数字段
- [monitoring-api.md](./monitoring-api.md) — `/api/serial/*`、`/api/tcp/*`
- `src/extensions/serialdev.md`、`src/extensions/tcpdev.md` — 设备命令说明
