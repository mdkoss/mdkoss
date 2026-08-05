# Modbus TCP Server 设备扩展（MDKOSS.Extensions.ModServer）

独立扩展程序集，通过统一扩展接口（`IMdkExtension` + `MdkExtensionHost`）注册设备类型 **`devmodserver`**：在本机提供 Modbus TCP Slave/Server，供外部 Master 读写线圈与寄存器；本机任务/监控也可直接读写同一数据区。

## 接入方式

宿主在 `new MdkRuntime` **之前**（或依赖 `DiscoverAndRegister` 扫描 `plugins/`）：

```csharp
using MDKOSS.Extensions.ModServer;

ModServerExtensionBootstrap.Register();
```

运行时插件 DLL：`MDKOSS.Extensions.ModServer.dll`（由 `MdkPlugins.targets` 复制到 `plugins/`，并附带 `NModbus` 依赖）。

## 目录

```text
src/MDKOSS.Extensions.ModServer/
├── MDKOSS.Extensions.ModServer.csproj
├── ModServerExtension.cs
├── ModServerDeviceActions.cs
├── ModServerDeviceApi.cs
├── devices/
│   ├── modserverdev.cs
│   └── modserver_device_parameters.cs
├── server/
│   └── api_modserver_module.cs   # /api/modserver/*
├── configs/
│   └── modserver.setting.json
├── modserver.md
└── README.md
```

## 配置

```json
{
  "id": "mod-1",
  "name": "Demo Modbus TCP Server",
  "type": "devmodserver",
  "enabled": true,
  "parameters": {
    "bindAddress": "0.0.0.0",
    "port": "1502",
    "unitId": "1",
    "autoStart": "true"
  }
}
```

| 参数 | 说明 | 默认 |
|------|------|------|
| `bindAddress` | 监听地址（`0.0.0.0` = 所有网卡） | `0.0.0.0` |
| `port` | TCP 端口（示例用 `1502`，避免占用需管理员权限的 `502`） | `502` |
| `unitId` | Modbus 从站地址 | `1` |
| `autoStart` | 设备 `Start` 时是否自动开始监听 | `true` |

运行示例：

```bat
run-src-mdkoss.bat --setting configs\modserver.setting.json
```

## 动作与 API

统一动作（`POST /api/devices/{id}/action`）：

| action | 说明 |
|--------|------|
| `start` / `listen` / `open` | 启动 Modbus TCP 监听 |
| `stop` / `close` | 停止监听 |
| `status` | 监听状态与参数 |
| `readHolding` / `writeHolding` | 保持寄存器（4x） |
| `readInput` / `writeInput` | 输入寄存器（3x，本机可写） |
| `readCoils` / `writeCoils` | 线圈（0x） |
| `readDiscrete` / `writeDiscrete` | 离散输入（1x，本机可写） |

读写动作参数示例：

```json
{ "address": 0, "count": 4 }
```

```json
{ "address": 0, "values": [100, 200, 300] }
```

```json
{ "address": 0, "values": [true, false, true] }
```

REST：

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/modserver/status?deviceId=` | 状态 |
| POST | `/api/modserver/start` | body: `{ "deviceId", "bindAddress?", "port?", "unitId?" }` |
| POST | `/api/modserver/stop` | body: `{ "deviceId" }` |
| POST | `/api/modserver/readHolding` | body: `{ "deviceId", "address", "count?" }` |
| POST | `/api/modserver/writeHolding` | body: `{ "deviceId", "address", "values": [...] }` |
| POST | `/api/modserver/readCoils` 等 | 对称；线圈写用 `boolValues` |

Vars（前缀 `device.{name}.{id}.`）：`isListening`、`bindAddress`、`port`、`unitId`、`lastError` 等。

## 注意

- 依赖 [NModbus](https://www.nuget.org/packages/NModbus) 实现协议栈。
- 端口 `502` 在 Windows 上可能需要提升权限；演示配置使用 `1502`。
- 外部 Master 使用标准 Modbus TCP 连接 `host:port`，Unit Id 与配置一致即可。
