# MDKOSS.Drivers.Dmc

雷赛 LTDMC 运动控制卡驱动（`DrvDmc` + `csLTDMC.LTDMC` / `LTDMC.dll`）。

配置 `type` 为 `dmc`。宿主通过插件发现注册，或显式：

```csharp
DmcDriverBootstrap.Register();
```

## GPIO 地址

与 GTS / SIM 同一套字符串，但 **`bit.{n}` 按雷赛手册从 0 起**，原样传给 API（不做 +1/−1）。SIM 默认 `ioBitBase=0`，与 DMC 相同；设 `ioBitBase=1` 可按 GTS 编号。

| 配置 address | LTDMC |
|---|---|
| `do.gpo.bit.{n}` | `dmc_write_outbit(card, n, 0\|1)` |
| `di.gpi.bit.{n}` | `dmc_read_inbit(card, n)` |
| `do.gpo` / `di.gpi` | `dmc_write_outport` / `dmc_read_inport`（port 0） |
| `di.home.bit.{n}` 等 | `dmc_axis_io_status`（轴号 `n`，同样从 0 起） |
| `do.enable.bit.{n}` | `dmc_write_sevon_pin(card, n, …)` |

```json
"out.tower.green": "drv-dmc|do.gpo.bit.0|绿灯",
"in.startButton": "drv-dmc|di.gpi.bit.0|启动"
```

GTS 卡上同一语义应写 `bit.1`。SIM 默认写 `bit.0`（`ioBitBase=1` 时与 GTS 相同）。

## 驱动 parameters

| 键 | 默认 | 说明 |
|---|---|---|
| `card` | `0` | 卡号 |
| `configPath` | | 可选，初始化时 `dmc_download_configfile` |
| `resetOnInit` | `false` | 为 true 时 `dmc_soft_reset` |
| `sevonActiveLow` | `true` | 伺服使能低电平有效 |

运行目录需能加载 `LTDMC.dll`。
