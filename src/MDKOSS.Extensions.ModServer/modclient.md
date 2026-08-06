# Modbus TCP Client（devmodclient）语义

本设备作为 **Modbus Master / Client**，主动连接远程 Slave，读取（及写回）四类数据区。重点能力是 **批读取**（多段地址一次请求）以及 **超长连续区自动分片**。

| 区 | 功能码 | 本机 API / action |
|----|--------|-------------------|
| Coils (0x) | 01 / 05 / 15 | `readCoils` / `writeCoils` |
| Discrete Inputs (1x) | 02 | `readDiscrete` |
| Holding Registers (4x) | 03 / 06 / 16 | `readHolding` / `writeHolding` |
| Input Registers (3x) | 04 | `readInput` |

## 生命周期

1. **Connect / open** — `TcpClient` + NModbus `IModbusMaster`
2. **Disconnect / close** — 关闭 TCP
3. **autoConnect=true** 时，运行时 `device.Start()` 自动 Connect

## 批读取

`readBatch` 接受多个 `{ area, address, count, tag? }`，在同一连接上顺序读取，单项失败不影响后续项。

连续区 `count` 超过协议上限时自动分片合并：

- 寄存器：每片 ≤ 125
- 线圈 / 离散：每片 ≤ 2000

## 地址

地址为 **0 基** ushort，与 `devmodserver` 本地数据区一致。
