# Modbus TCP Server（devmodserver）语义

本设备作为 **Modbus Slave / Server**，在指定 TCP 端口上接受 Master 请求，并维护四类数据区：

| 区 | 功能码（Master） | 本机 API |
|----|------------------|----------|
| Coils (0x) | 01 / 05 / 15 | `readCoils` / `writeCoils` |
| Discrete Inputs (1x) | 02 | `readDiscrete` / `writeDiscrete`（本机可预置） |
| Holding Registers (4x) | 03 / 06 / 16 | `readHolding` / `writeHolding` |
| Input Registers (3x) | 04 | `readInput` / `writeInput`（本机可预置） |

## 生命周期

1. **StartServer / listen** — `TcpListener` + NModbus `IModbusSlaveNetwork.ListenAsync`
2. **StopServer** — 取消监听并释放端口
3. **autoStart=true** 时，运行时 `device.Start()` 自动调用 StartServer

## 地址

地址为 **0 基** ushort。单次读写长度受 NModbus / Modbus 协议限制（寄存器通常 ≤ 125，线圈 ≤ 2000）。
