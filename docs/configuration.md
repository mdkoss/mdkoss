# 配置模型

工程行为由 JSON 配置文件描述，通过 `MdkSetting.Load(path)` 反序列化。默认路径为可执行文件目录下的 `configs/sample.setting.json`（`MdkSetting.DefaultSettingsPath`）。

## 顶层字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `projectName` | string | 工程显示名称（区分不同机型/工程） |
| `startPage` | string? | CEF/监控首页，如 `indexDieBonder.html`；缺省为 `index.html` |
| `monitoringPrefix` | string? | 监控 HTTP 前缀，须以 `/` 结尾，如 `http://127.0.0.1:5081/`。默认 `http://127.0.0.1:5080/` |
| `cycleMs` | int | 主循环周期提示（毫秒），默认 20 |
| `databasePath` | string? | SQLite 路径，默认 `data/mdk.db` |
| `drivers` | array | 驱动列表 |
| `devices` | array | 设备列表 |
| `tasks` | array | 任务列表 |
| `vars` | object | 启动时写入变量中心的初始值 |
| `recipeVarKeys` | string[] | 参与配方管理的 vars 键子集；为空时从所有 recipe 推断 |
| `activeRecipeId` | string? | 启动时自动应用的配方 id |
| `recipes` | array | 命名配方预设 |

## drivers[]

```json
{
  "id": "sim1",
  "type": "sim",
  "enabled": true,
  "parameters": { }
}
```

| 字段 | 说明 |
|------|------|
| `id` | 驱动唯一标识，设备与任务通过此 id 引用 |
| `type` | 驱动类型，见下表 |
| `enabled` | 为 false 时不实例化 |
| `parameters` | 驱动专属键值，由各类 `IDriver.Initialize` 解析 |

### 内置驱动类型

| type | 实现 | 用途 |
|------|------|------|
| `sim` | `DrvSim`（`MDKOSS.Drivers.Sim`） | 软件仿真：内存 DI/DO、轴等 |
| `gts` | `DrvGts`（`MDKOSS.Drivers.Gts`） | GTS 运动卡驱动（gts.dll） |
| `dmc` | （待 `DrvDmc`） | LTDMC 原生绑定在 `MDKOSS.Drivers.Dmc`，IDriver 包装待补 |

扩展新驱动：新建 `src/MDKOSS.Drivers.Xxx`，实现 `IDriver` + `IMdkExtension`（`registration.Driver`），宿主调用 `XxxDriverBootstrap.Register()`。

## devices[]

```json
{
  "id": "gpio1",
  "name": "主 IO",
  "type": "gpio",
  "driverId": "sim1",
  "enabled": true,
  "parameters": { }
}
```

| 字段 | 说明 |
|------|------|
| `id` / `name` | 设备 id 与显示名 |
| `type` | 设备类型（大小写不敏感） |
| `driverId` | 单驱动设备的默认驱动；平台设备可作为各轴默认驱动 |
| `enabled` | 为 false 时不实例化 |
| `parameters` | 类型相关参数块 |

### 设备类型一览

| type | 所在程序集 | 说明 |
|------|------------|------|
| `gpio` | Core | 多驱动物理 IO 路由，`in.*` / `out.*` 映射到 `driverId:address` |
| `vio` | Core | 虚拟 IO，单驱动，地址形如 `vio.{deviceId}.in\|out.{alias}` |
| `axis` | Core | 单轴设备 |
| `platform` | Core | 多轴平台；也支持 `xy` / `xyz` / `xyzu` / `xyzuv` / `xyzuvw` 作为 type 简写 |
| `cameradev` | Core | 相机类设备占位 |
| `serialdev` | Extensions.Serial | RS-232C 串口 |
| `tcpdev` | Extensions.Tcp | TCP 客户端/服务端通信 |
| `extcamera` | Extensions.Camera | 扩展相机（仿真 open/trigger；见 `src/MDKOSS.Extensions.Camera`） |
| `devpyscript` | Extensions.PyScript | 外部进程执行 Python 脚本（见 `src/MDKOSS.Extensions.PyScript`） |
| `devmodserver` | Extensions.ModServer | Modbus TCP Server/Slave（见 `src/MDKOSS.Extensions.ModServer`） |

### gpio parameters

- `in.{alias}` / `out.{alias}`：值为 `driverId:address` 或物理地址表达式
- 可选 `driverIds`：逗号分隔，限定本 GPIO 设备可见的驱动子集

解析：`GpioDeviceParameterSet`（`gpio_device_parameters.cs`）

### vio parameters

- `in.*` / `out.*` 取值须为空或 `virtual`
- 禁止物理 `driverId:address` 路由

解析：`VioDeviceParameterSet`

### platform parameters

- `kind` 或 type 简写：`xy`、`xyz`、`xyzu`、`xyzuv`、`xyzuvw`
- `axis.X`、`axis.Y` 等：按轴指定驱动 id；未指定时使用顶层 `driverId`

解析：`PlatformDeviceParameterSet`

### serialdev parameters

`portName`、`baudRate`、`dataBits`、`parity`、`stopBits`、`readTimeout`、`writeTimeout`、`dtrEnable`、`rtsEnable` 等。

解析：`SerialDeviceParameterSet`（`src/MDKOSS.Extensions.Serial`）

### tcpdev parameters

主机、端口、连接模式等，见 `TcpDeviceParameterSet`（`src/MDKOSS.Extensions.Tcp`）与 `tcpdev.md`。

### extcamera parameters

| 参数 | 说明 | 默认 |
|------|------|------|
| `backend` | 后端标记（示例实现 `sim`） | `sim` |
| `deviceIndex` | 设备序号 | `0` |
| `width` / `height` | 分辨率 | `1280` / `720` |
| `exposureMs` | 曝光（ms，仿真占位） | `10` |
| `noisePx` | 仿真偏移噪声幅度 | `0.5` |

解析：`ExtCameraDeviceParameters`（`src/MDKOSS.Extensions.Camera`）。与 Core 内置 `cameradev` 占位设备不同。

### devpyscript parameters

| 参数 | 说明 | 默认 |
|------|------|------|
| `pythonPath` | Python 可执行文件 | `python` |
| `scriptPath` | 默认脚本路径 | 空 |
| `workingDirectory` | 工作目录；空则用脚本目录 | 空 |
| `arguments` | 额外命令行参数 | 空 |
| `timeoutMs` | 超时毫秒；`0` 不超时 | `30000` |
| `captureOutput` | 是否捕获 stdout/stderr | `true` |

解析：`PyScriptDeviceParameters`（`src/MDKOSS.Extensions.PyScript`）。

### devmodserver parameters

| 参数 | 说明 | 默认 |
|------|------|------|
| `bindAddress` | 监听地址 | `0.0.0.0` |
| `port` | TCP 端口 | `502` |
| `unitId` | Modbus 从站地址 | `1` |
| `autoStart` | 设备 Start 时自动监听 | `true` |

解析：`ModServerDeviceParameters`（`src/MDKOSS.Extensions.ModServer`）。

## tasks[]

```json
{
  "name": "poll_sim",
  "type": "pollDriver",
  "driverId": "sim1",
  "intervalMs": 100,
  "parameters": { }
}
```

| 字段 | 说明 |
|------|------|
| `name` | 任务名，运行时唯一 |
| `type` | 任务类型，默认 `pollDriver` |
| `driverId` | 关联驱动（视任务类型而定） |
| `intervalMs` | 调度间隔 |
| `parameters` | 任务专属参数 |

### 内置任务类型

| type 别名 | 实现 | 说明 |
|-----------|------|------|
| `poll` / `pollDriver` | `PollDriverTask` | 驱动心跳/轮询 |
| `operation` / `taskOperation` | `TaskOperation` | 操作序列任务 |
| `cycle` / `taskCycle` | `TaskCycle` | 周期循环任务 |

注册扩展：`RuntimeTaskFactory.Register(type, factory)`。

## vars 与 recipes

`vars` 为运行时键值字典，启动时写入 `MVarStore`。

配方（`recipes[]`）是 vars 子集的命名快照：

```json
{
  "recipeVarKeys": ["recipe.speed", "recipe.mode"],
  "activeRecipeId": "default",
  "recipes": [
    {
      "id": "default",
      "name": "默认",
      "vars": {
        "recipe.speed": 100,
        "recipe.mode": "auto"
      }
    }
  ]
}
```

应用配方时写入 vars，并设置 `recipe.activeId` / `recipe.activeName`。HTTP 管理见 [monitoring-api.md](./monitoring-api.md) 配方端点。

## 配置与持久化

- 启动时 `MdkDataStore.SyncRecipesWithSetting` 将 JSON 中的 recipes 与 SQLite 对齐
- 退出 `Dispose` 时 `PersistRecipesFromSetting` 写回数据库
- 排单、示教点仅存 SQLite，不嵌入 setting JSON
- CEF `man_*` 页通过 `/api/config` 轻量 PATCH 内存中的 `MdkSetting`，`POST /api/config/save` 写回 JSON（`MdkRuntime.SettingPath`）并尝试导出到 SQLite 配置表；**不热替换**已运行驱动/设备，保存后需重启运行时

详见 [data-persistence.md](./data-persistence.md) 与 [monitoring-api.md](./monitoring-api.md)。

## 验证

WinForms `ComponentConfigForm` 在保存前校验：重复 id、缺失驱动、非法 interval、parameters 格式等。运行时 `BootstrapDevices` 对无法解析的配置会跳过或抛出 `MdkException`（视错误类型而定）。

## 示例

完整示例见 `src/MDKOSS.Sample/configs/sample.setting.json`。
