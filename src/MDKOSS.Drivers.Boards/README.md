# MDKOSS.Drivers.Boards

国内设备厂（点胶 / PNP / 装配）常用运动卡的 **type 插件**，一个 DLL 覆盖固高 GTS、雷赛 LTDMC 与目录卡（正运动 / 众为兴 / 摩信 / EtherCAT 等）。

```csharp
BoardDriverBootstrap.Register();  // 目录卡（zmc / adt / mpc / …）
GtsDriverBootstrap.Register();    // gts
DmcDriverBootstrap.Register();    // dmc
```

或放入 `plugins/`，由 `DiscoverAndRegister` 加载（扩展 id：`driver-boards` / `driver-gts` / `driver-dmc`）。

## 目录

| 子目录 | 内容 |
|--------|------|
| 根目录 | 目录卡：`BoardCatalog` + `BoardCardDriver`（仿真 / 原生二选一） |
| `Gts/` | 固高 GTS 脉冲卡 `DrvGts`（gts.dll，P/Invoke 内联在驱动里） |
| `Dmc/` | 雷赛 LTDMC `DrvDmc` + `DmcIoMap` + 厂商绑定 `csLTDMC.LTDMC`（LTDMC.dll） |
| `Native/` | 目录卡各厂商原生后端（动态加载，不附带 SDK） |

`Gts` / `Dmc` 面向真卡，运行目录需能加载对应厂商 DLL；目录卡默认 `simulate=true`，IO / 点位 / 点动走内存。

## 注册的 type

| type | 厂商 / 系列 | 常见型号 | 总线 | 自备 DLL（live） | bit 默认 |
|------|-------------|----------|------|------------------|----------|
| `gts` | 固高 GTS | GTS-800 / 400 系列 | 脉冲 PCI | `gts.dll` | 1 |
| `dmc` | 雷赛 LTDMC | DMC3000 / DMC5x10 | 脉冲 PCI | `LTDMC.dll` | 0 |
| `zmc` / `zmotion` | 正运动 Zmotion | ZMC / ZMIO / EtherCAT 控制器 | 脉冲 / EtherCAT | `zauxdll.dll` | 0 |
| `adt` | 众为兴 | ADT-8948 / 8960 / 8940 | 脉冲 PCI | `adt8948a1.dll` | 0 |
| `mpc` | 摩信 | MPC08 / MPC2810 | 脉冲 ISA/PCI | `MPC08.dll` | 0 |
| `emc` | 雷赛 EtherCAT | EMC / 总线型 LTDMC | EtherCAT | `LTDMC.dll` | 0 |
| `gtn` | 固高总线 | GTN / GLink | EtherCAT | `gtn.dll` | 1（同 GTS） |
| `adlink` | 凌华 | PCI-8254 / AMP-204C | 脉冲 | `APS168.dll` | 0 |
| `advantech` | 研华 | PCI-1240 / 1245 | 脉冲 | `ADVMOT.dll` | 0 |
| `galil` | Galil | DMC-40x0 / DMC-21x3 | 以太网 | `gclib.dll` | 0 |
| `inovance` | 汇川 | IMC30G / IMC60G | EtherCAT | `IMC_API_x64.dll` | 0 |

其它内核插件不在本项目：`sim` / `vio` 见 `MDKOSS.Drivers.Sim`，`s7` / `s7-1200` 见 `MDKOSS.Drivers.S7`。

## GPIO 地址

三类卡共用 `di.gpi.bit.n` / `do.gpo.bit.n` 字符串，差别只在 **bit 起始号**：固高点号从 1 起（`ioBitBase=1`），雷赛与目录卡从 0 起。

```json
"out.tower.green": "drv-dmc|do.gpo.bit.0|绿灯",
"in.startButton": "drv-dmc|di.gpi.bit.0|启动"
```

同一语义在 GTS 卡上写 `bit.1`。SIM 默认 `ioBitBase=0`（与 DMC 相同），设 `1` 可按 GTS 编号。

DMC 地址映射（原样传给 API，不做 ±1）：

| 配置 address | LTDMC |
|---|---|
| `do.gpo.bit.{n}` | `dmc_write_outbit(card, n, 0\|1)` |
| `di.gpi.bit.{n}` | `dmc_read_inbit(card, n)` |
| `do.gpo` / `di.gpi` | `dmc_write_outport` / `dmc_read_inport`（port 0） |
| `di.home.bit.{n}` 等 | `dmc_axis_io_status`（轴号 `n`，同样从 0 起） |
| `do.enable.bit.{n}` | `dmc_write_sevon_pin(card, n, …)` |

## parameters

目录卡（`zmc` / `adt` / …）：

| 键 | 默认 | 说明 |
|----|------|------|
| `simulate` | `true` | 内存仿真；`false` 时调用对应厂商 P/Invoke（自备 DLL） |
| `card` | `0` | 卡号 |
| `nativeDll` | 见上表 | 覆盖默认 DLL 文件名 |
| `ioBitBase` | 按卡 | `0` 或 `1` |
| `ip` | | 网口 / EtherCAT 主站 IP（预留） |
| `note` | | 备注 |

`gts`：

| 键 | 默认 | 说明 |
|----|------|------|
| `cardNo` | `1` | 卡号 |
| `channel` | `0` | 通道 |
| `crd` | `1` | 插补坐标系 |
| `openParam` | `0` | `GT_Open` 参数 |
| `resetOnInit` | `false` | 为 true 时 `GT_Reset` |

`dmc`：

| 键 | 默认 | 说明 |
|----|------|------|
| `card` | `0` | 卡号 |
| `crd` | `0` | 插补坐标系 |
| `configPath` | | 可选，初始化时 `dmc_download_configfile` |
| `resetOnInit` | `false` | 为 true 时 `dmc_soft_reset` |
| `sevonActiveLow` | `true` | 伺服使能低电平有效 |

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

目录卡 `simulate=false` 且目录里没有厂商 DLL 时 `IsConnected=false`，`driver.lastError` 为 `native_dll_missing`（或 `native_entry_missing` / `open_failed`）。函数对照见 [docs/drivers.md](../../docs/drivers.md)。
