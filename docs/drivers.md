# 运动控制卡与驱动

PC 上位机（点胶 / PNP / 贴片）侧常见卡型，以及 MDKOSS 的 `drivers[].type`。

厂商 SDK **不进仓库**。板卡 type 统一由 `MDKOSS.Drivers.Boards` 注册：`gts` / `dmc` 已接原生 DLL，其余卡默认仿真。

## 怎么选 type

| 现场板卡 | 配 `type` | 插件 |
|----------|-----------|------|
| 无卡 / 先调工艺 | `sim` | Sim |
| 虚拟 IO 卡 | `vio` | Sim |
| 固高 GTS 脉冲（GTS-400 / 800 等） | `gts` | Boards |
| 固高 EtherCAT / GLink | `gtn` | Boards |
| 雷赛脉冲 LTDMC（DMC1000 / 2410 / 3000） | `dmc` | Boards |
| 雷赛 EtherCAT 主站 | `emc` | Boards |
| 正运动 ZMC / 总线控制器 | `zmc` 或 `zmotion` | Boards |
| 众为兴 ADT-8948 / 8960 | `adt` | Boards |
| 摩信 MPC08 / 2810 | `mpc` | Boards |
| 凌华 PCI-8254 / AMP | `adlink` | Boards |
| 研华 PCI-1240 / 1245 | `advantech` | Boards |
| Galil DMC 以太网 | `galil` | Boards |
| 汇川 EtherCAT / AM | `inovance` | Boards |
| 西门子 S7 作 IO | `s7` / `s7-1200` | S7 |

任务与设备不要写厂商 API。轴 / GPIO 走 `IDriver`，换卡改 `type` 和地址基址。

## 市面分组（国内设备软件）

**脉冲卡（PCI/PCIe，工控机里最常见）**

- 固高 GTS、雷赛 DMC、正运动（部分）、众为兴 ADT、摩信 MPC、凌华、研华

**总线主站（EtherCAT，多轴 / 分布式 IO）**

- 固高 GTN、雷赛 EMC、正运动 EtherCAT、汇川、部分欧系（TwinCAT 不在本仓库）

**以太网独立控制器**

- Galil、部分正运动 ZMC、部分固高

**PLC 当 IO（运动在 PLC 或伺服总线）**

- 西门子 S7：用 `s7`，不要当运动卡

## 地址习惯

| 卡 | `bit.n` |
|----|---------|
| 固高 GTS / GTN | 从 **1**（`ioBitBase=1`） |
| 雷赛 DMC / EMC、正运动、众为兴、摩信、默认 Boards | 从 **0** |

同一套 `di.gpi.bit.n` / `do.gpo.bit.n`。换卡先对手册改 n，不要改任务。

## 扩展真卡

`MDKOSS.Drivers.Boards` 已按公开手册接了各厂 **最小 P/Invoke**（开卡 / GPIO / 使能 / 点位 / 点动 / 停）。厂商 SDK **不进仓库**。

1. 把下表 DLL 放到 exe 或 `plugins/`。
2. 配置 `"simulate": "false"`，需要时写 `ip` / `card` / `nativeDll`。
3. 无 DLL → `native_dll_missing`；函数名与现场 SDK 不一致 → `native_entry_missing`。可用 `nativeDll` 覆盖文件名。

| type | 自备 DLL | 开卡 | 点位 | 来源 |
|------|----------|------|------|------|
| `zmc` | `zauxdll.dll` | `ZAux_OpenEth` / `OpenPci` | `ZAux_Direct_Single_MoveAbs` | 正运动 ZAux 手册 |
| `adt` | `adt8948a1.dll` | `adt8948_initial` | `pmove`（轴号 1 起） | ADT-8948A1 手册 |
| `mpc` | `MPC08.dll` | `auto_set` + `init_board` | `fast_pmove` | MPC08SP 手册 |
| `emc` | `LTDMC.dll` | `dmc_board_init` | `dmc_pmove` | 与现有 Dmc 相同 |
| `gtn` | `gtn.dll` | `GTN_Open` | `GTN_PrfTrap` + `GTN_Update` | 固高 GTN 手册 |
| `adlink` | `APS168.dll` | `APS_initial` | `APS_absolute_move` | 凌华 APS 库 |
| `advantech` | `ADVMOT.dll` | `Acm_DevOpen` | `Acm_AxMoveAbs` | 研华 Common Motion |
| `galil` | `gclib.dll` | `GOpen` | `PA` / `BG` | Galil gclib |
| `inovance` | `IMC_API_x64.dll` | `IMC_Open` | `IMC_SetAxPtpAbs` | 汇川 IMC 公开笔记 |

插补仍走 `IDriver` 默认 `false`。现场函数名与手册不符时只改对应 `Native/*.cs`，不要改任务。
