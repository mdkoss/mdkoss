# MDKOSS.Drivers.Boards

国内设备厂（点胶 / PNP / 装配）常用运动卡的 **type 插件**。默认 `simulate=true`，IO / 点位 / 点动走内存，**不附带厂商 SDK**。真卡绑定后由用户自备 DLL。

```csharp
BoardDriverBootstrap.Register();
```

或放入 `plugins/`，由 `DiscoverAndRegister` 加载（`driver-boards`）。

## 已有内核插件（不在本项目）

| type | 项目 | 厂商 |
|------|------|------|
| `sim` / `vio` | `MDKOSS.Drivers.Sim` | 软件仿真 |
| `gts` | `MDKOSS.Drivers.Gts` | 固高 GTS 脉冲卡（gts.dll） |
| `dmc` | `MDKOSS.Drivers.Dmc` | 雷赛 LTDMC 脉冲卡 |
| `s7` / `s7-1200` | `MDKOSS.Drivers.S7` | 西门子 PLC（IO，非运动卡） |

## 本插件注册的 type

| type | 厂商 / 系列 | 常见型号 | 总线 | 自备 DLL（live） | bit 默认 |
|------|-------------|----------|------|------------------|----------|
| `zmc` / `zmotion` | 正运动 Zmotion | ZMC / ZMIO / EtherCAT 控制器 | 脉冲 / EtherCAT | `zauxdll.dll` | 0 |
| `adt` | 众为兴 | ADT-8948 / 8960 / 8940 | 脉冲 PCI | `adt8948a1.dll` | 0 |
| `mpc` | 摩信 | MPC08 / MPC2810 | 脉冲 ISA/PCI | `MPC08.dll` | 0 |
| `emc` | 雷赛 EtherCAT | EMC / 总线型 LTDMC | EtherCAT | `LTDMC.dll` | 0 |
| `gtn` | 固高总线 | GTN / GLink | EtherCAT | `gtn.dll` | 1（同 GTS） |
| `adlink` | 凌华 | PCI-8254 / AMP-204C | 脉冲 | `APS168.dll` | 0 |
| `advantech` | 研华 | PCI-1240 / 1245 | 脉冲 | `ADVMOT.dll` | 0 |
| `galil` | Galil | DMC-40x0 / DMC-21x3 | 以太网 | `gclib.dll` | 0 |
| `inovance` | 汇川 | IMC30G / IMC60G | EtherCAT | `IMC_API_x64.dll` | 0 |

GPIO 字符串与 SIM/DMC 相同：`di.gpi.bit.n` / `do.gpo.bit.n`。`gtn` 默认 `ioBitBase=1`（固高点号从 1 起）。

## parameters

| 键 | 默认 | 说明 |
|----|------|------|
| `simulate` | `true` | 内存仿真；`false` 时调用对应厂商 P/Invoke（自备 DLL） |
| `card` | `0` | 卡号 |
| `nativeDll` | 见上表 | 覆盖默认 DLL 文件名 |
| `ioBitBase` | 按卡 | `0` 或 `1` |
| `ip` | | 网口 / EtherCAT 主站 IP（预留） |
| `note` | | 备注 |

```json
{
  "id": "drv-zmc",
  "type": "zmc",
  "enabled": true,
  "parameters": {
    "simulate": "true",
    "card": "0"
  }
}
```

`simulate=false` 且目录里没有厂商 DLL 时 `IsConnected=false`，`driver.lastError` 为 `native_dll_missing`（或 `native_entry_missing` / `open_failed`）。函数对照见 [docs/drivers.md](../../docs/drivers.md)。
