# MDKOSS.Sample.Modbus — Modbus IDriver 联调

本 Sample 用 **Modbus TCP Master**（`type=modbus-tcp`）联调 Holding Register：默认仿真 200 个字（0..199），也可改 `host`/`port` 接现场 Slave。寄存器点位按 `reg` / `regi` / `regf` / `bit` 组态，操作台走 JSON 面板。

驱动实现来自 `MDKOSS.Extensions.ModServer` 插件，本工程只提供宿主、HMI 与 `/api/modbusdrv`。

## 1. 组件

| 配置 ID | 类型 | 职责 |
|---------|------|------|
| `drv-modbus` | `modbus-tcp` | Master；`simulate=true` 时内存仿真，无需 Slave |
| `gpio-modbus` | gpio | 把 HR/Coil 映射成 DI/DO 示例点 |
| `poll-modbus` | pollDriver | 200ms 驱动心跳 |

```text
src/MDKOSS.Sample.Modbus/
├── Modbus/                    # 扩展：API、寄存器目录、Holding 批读写
├── configs/sample.setting.json
└── views/
    ├── debug_modbus_holding.html   # 默认启动页：200 Holding
    └── indexModbus.html            # 寄存器分组 / 操作台组态
```

`Program.Main` 在插件发现后 `Register(new ModbusDriverSampleExtension())`。

## 2. 界面与 API

| 能力 | 路径 |
|------|------|
| Holding 联调页 | `/debug_modbus_holding.html`（配置 `startPage`） |
| 寄存器组态 / 操作台 | `/indexModbus.html` |
| 读 Holding | `GET /api/modbusdrv/holding?start=0&count=200` |
| 点位目录 | `GET /api/modbusdrv/catalog` |
| 点位当前值 | `GET /api/modbusdrv/values` |
| 写一字 / 批量 / 按点 | `POST /api/modbusdrv/write` · `writemany` · `writepoint` |
| 填充测试图案 | `POST /api/modbusdrv/fill` |

点位类型：`reg`（16 位）、`regi`（32 位整数，两字）、`regf`（float）、`bit`（字内位）、`di`/`do`（按 16 位处理）。

## 3. 配置

| 文件 | 说明 |
|------|------|
| `configs/sample.setting.json` | 驱动 / GPIO / 轮询任务；默认 `simulate=true` |
| `configs/plc_registers.json` | 命名点位表（现场地址，**不入库**） |
| `configs/plc_panels.json` | 操作台面板组态（**不入库**） |
| `configs/modbus.layout.json` | HMI 控件布局（**不入库**） |

接真机：把 `simulate` 改为 `false`，改 `host` / `port` / `unitId`。无 `plc_registers.json` 时仍可用 Holding 页直接按地址读写。

## 4. 运行

```bash
dotnet run --project src/MDKOSS.Sample.Modbus/MDKOSS.Sample.Modbus.csproj

dotnet run --project src/MDKOSS.Sample.Modbus/MDKOSS.Sample.Modbus.csproj -- --console
```

监控入口：`http://127.0.0.1:5090/debug_modbus_holding.html`

系统页与 MySQL 调试仍走 Cef 公共 `views/`（如 `/index.html`、`/debug_mysql.html`）。
