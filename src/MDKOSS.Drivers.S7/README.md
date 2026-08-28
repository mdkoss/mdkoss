# MDKOSS.Drivers.S7

西门子 S7-1200（及同族 S7）PLC 驱动：`DrvS7` + [S7netplus](https://github.com/s7netplus/s7netplus)（ISO-on-TCP / 端口 102）。

配置 `type` 为 `s7` 或 `s7-1200`。宿主通过插件发现注册，或显式：

```csharp
S7DriverBootstrap.Register();
```

## GPIO 地址

与 SIM / DMC 同一套 `DriverIoAddress` 字符串；`bit.{n}` 默认从 0 起（可用 `ioBitBase` 改为 1）。

| 配置 address | PLC |
|---|---|
| `di.gpi.bit.{n}` | 输入映像区 I，自 `diByteBase` 起的第 n 位 |
| `do.gpo.bit.{n}` | 输出映像区 Q，自 `doByteBase` 起的第 n 位 |
| `di.gpi` / `do.gpo` | 连续 4 字节拼成 int 位掩码 |

也支持原生 S7 地址经 `TryRead` / `Write`：`I0.0`、`Q0.0`、`IB0`、`QB0`、`DB1.DBX0.0`、`MW10` 等。
仿真模式下 I/Q 走映像区字节表，Merker / DB（`M*`、`DB*.DBX/DBB/DBW/DBD`）走内存袋，便于无 PLC 联调。

轴运动接口对本驱动返回 false（PLC 作 IO 底层，不模拟运动卡）。

## 驱动 parameters

| 键 | 默认 | 说明 |
|---|---|---|
| `host` | | PLC IP；空且未强制连网时走内存仿真 |
| `rack` | `0` | 机架号 |
| `slot` | `1` | 槽位（S7-1200 集成 PN 常用 0 或 1） |
| `cpu` | `S71200` | `S71200` / `S71500` / `S7300` / `S7400` / `S7200` |
| `simulate` | 无 host 时为 true | `true` 强制内存仿真，不连真实 PLC |
| `diByteBase` | `0` | DI 端口字起始字节 |
| `doByteBase` | `0` | DO 端口字起始字节 |
| `ioBitBase` | `0` | `bit.n` 编号基准（0 或 1） |
| `readTimeoutMs` | `3000` | 读超时 |
| `writeTimeoutMs` | `3000` | 写超时 |

```json
{
  "id": "drv-s7",
  "type": "s7-1200",
  "enabled": true,
  "parameters": {
    "host": "192.168.0.1",
    "rack": "0",
    "slot": "1"
  }
}
```

无现场 PLC 时设 `"simulate": "true"`，IO 在内存中往返，便于联调与单测。
