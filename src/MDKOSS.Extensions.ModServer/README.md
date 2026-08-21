# Modbus TCP 扩展（MDKOSS.Extensions.ModServer）

独立扩展程序集，通过统一扩展接口（`IMdkExtension` + `MdkExtensionHost`）注册两类设备与一套 IO 驱动：

| 配置 type | 角色 | 说明 |
|-----------|------|------|
| **`devmodserver`** | Slave / Server | 本机监听，供外部 Master 读写 |
| **`devmodclient`** | Master / Client | 连接远程 Slave，**重点支持批读取** |
| **`modbus` / `modbus-tcp`** | IDriver | `DrvModbus`：以 Modbus TCP 作 GPIO 底层（线圈/离散量/保持寄存器） |

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
├── drvmodbus.cs                 # IDriver type=modbus / modbus-tcp
├── ModServerDeviceActions.cs / ModServerDeviceApi.cs
├── ModClientDeviceActions.cs / ModClientDeviceApi.cs
├── devices/
│   ├── modserverdev.cs / modserver_device_parameters.cs
│   └── modclientdev.cs / modclient_device_parameters.cs
├── server/
│   ├── api_modserver_module.cs   # /api/modserver/*
│   └── api_modclient_module.cs   # /api/modclient/*
├── configs/
│   ├── modserver.setting.json
│   └── modclient.setting.json
├── modserver.md / modclient.md
└── README.md
```

---

## devmodserver（Server）

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
| `port` | TCP 端口（示例用 `1502`） | `502` |
| `unitId` | Modbus 从站地址 | `1` |
| `autoStart` | 设备 `Start` 时是否自动监听 | `true` |

统一动作：`start` / `stop` / `status` / `readHolding` / `writeHolding` / `readInput` / `writeInput` / `readCoils` / `writeCoils` / `readDiscrete` / `writeDiscrete`

REST：`/api/modserver/*`（见 `modserver.md`）

---

## devmodclient（Client，批读取）

```json
{
  "id": "modc-1",
  "name": "Demo Modbus TCP Client",
  "type": "devmodclient",
  "enabled": true,
  "parameters": {
    "host": "127.0.0.1",
    "port": "1502",
    "unitId": "1",
    "connectTimeoutMs": "3000",
    "readTimeoutMs": "3000",
    "writeTimeoutMs": "3000",
    "autoConnect": "true"
  }
}
```

| 参数 | 说明 | 默认 |
|------|------|------|
| `host` | 远程 Slave 主机 | `127.0.0.1` |
| `port` | 远程端口 | `502` |
| `unitId` | 从站地址 | `1` |
| `connectTimeoutMs` | 连接超时 | `3000` |
| `readTimeoutMs` / `writeTimeoutMs` | 读写超时 | `3000` |
| `autoConnect` | 设备 `Start` 时是否自动连接 | `true` |

### 统一动作（`POST /api/devices/{id}/action`）

| action | 说明 |
|--------|------|
| `connect` / `open` | 连接远程 Slave |
| `disconnect` / `close` | 断开 |
| `status` | 连接状态与参数 |
| `readHolding` / `writeHolding` | 保持寄存器（超长自动分片） |
| `readInput` | 输入寄存器（超长自动分片） |
| `readCoils` / `writeCoils` | 线圈 |
| `readDiscrete` | 离散输入 |
| **`readBatch`** | **多段批读取（核心）** |

单次读：

```json
{ "address": 0, "count": 200 }
```

批读取：

```json
{
  "items": [
    { "tag": "temp", "area": "holding", "address": 0, "count": 10 },
    { "tag": "flags", "area": "coils", "address": 0, "count": 8 },
    { "tag": "ai", "area": "input", "address": 100, "count": 4 }
  ]
}
```

`area` / `kind` / `type` 可用：`holding` | `input` | `coils` | `discrete`（及 `4x`/`3x`/`0x`/`1x` 等别名）。单项失败不影响后续项，结果带 `success` / `error`。

### REST

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/modclient/status?deviceId=` | 状态 |
| POST | `/api/modclient/connect` | body: `{ "deviceId", "host?", "port?", "unitId?" }` |
| POST | `/api/modclient/disconnect` | body: `{ "deviceId" }` |
| POST | `/api/modclient/readHolding` | `{ "deviceId", "address", "count?" }` |
| POST | `/api/modclient/writeHolding` | `{ "deviceId", "address", "values": [...] }` |
| POST | `/api/modclient/readInput` / `readCoils` / `readDiscrete` | 对称 |
| POST | `/api/modclient/writeCoils` | `{ "deviceId", "address", "boolValues": [...] }` |
| POST | `/api/modclient/readBatch` | `{ "deviceId", "items": [ ... ] }` |

Vars（前缀 `device.{name}.{id}.`）：`isConnected`、`host`、`port`、`unitId`、`lastError`、`lastBatchCount` 等。

运行示例（本机 server + client）：

```bat
run-src-mdkoss.bat --setting configs\modclient.setting.json
```

先向 server 写保持寄存器，再批读 client：

```http
POST /api/modserver/writeHolding
{ "deviceId": "mod-1", "address": 0, "values": [11, 22, 33, 44] }

POST /api/modclient/readBatch
{
  "deviceId": "modc-1",
  "items": [
    { "tag": "hr0", "area": "holding", "address": 0, "count": 4 }
  ]
}
```

## 注意

- 依赖 [NModbus](https://www.nuget.org/packages/NModbus) 实现协议栈。
- 端口 `502` 在 Windows 上可能需要提升权限；演示配置使用 `1502`。
- 连续寄存器读超过 125、线圈超过 2000 时，client 会自动分片请求并拼接结果。

---

## IDriver（`modbus` / `modbus-tcp`）

`DrvModbus` 作为 Modbus TCP **Master**，把远程 Slave 的线圈/离散输入（或保持寄存器）映射为运行时统一 GPIO 地址，便于 GPIO 设备与任务直接使用，无需再包一层 `devmodclient`。

| 配置 address | Modbus（默认） |
|---|---|
| `di.gpi.bit.{n}` | 离散输入，自 `diAddress` 起的第 n 位 |
| `do.gpo.bit.{n}` | 线圈，自 `doAddress` 起的第 n 位 |
| `di.gpi` / `do.gpo` | 连续 32 位拼成 int 位掩码 |

原生地址：`coil.{n}`、`discrete.{n}`、`holding.{n}`、`input.{n}`。

| 参数 | 默认 | 说明 |
|---|---|---|
| `host` | | Slave IP；空且未强制连网时走内存仿真 |
| `port` | `502` | TCP 端口 |
| `unitId` | `1` | 从站地址 |
| `simulate` | 无 host 时为 true | `true` 强制内存仿真 |
| `diAddress` / `doAddress` | `0` | DI/DO 起始地址 |
| `diArea` | `discrete` | `discrete` / `coils` / `holding` |
| `doArea` | `coils` | `coils` / `holding` |
| `ioBitBase` | `0` | `bit.n` 编号基准（0 或 1） |

```json
{
  "id": "drv-modbus",
  "type": "modbus-tcp",
  "enabled": true,
  "parameters": {
    "host": "192.168.0.10",
    "port": "502",
    "unitId": "1"
  }
}
```

无现场 Slave 时设 `"simulate": "true"`，IO 在内存中往返，便于联调与单测。轴运动接口对本驱动返回 false。
