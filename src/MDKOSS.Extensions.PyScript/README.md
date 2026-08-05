# Python 脚本设备扩展（MDKOSS.Extensions.PyScript）

独立扩展程序集，通过统一扩展接口（`IMdkExtension` + `MdkExtensionHost`）注册设备类型 **`devpyscript`**：以外部进程方式执行 Python 脚本，捕获 stdout/stderr/exitCode。

## 接入方式

宿主在 `new MdkRuntime` **之前**（或依赖 `DiscoverAndRegister` 扫描 `plugins/`）：

```csharp
using MDKOSS.Extensions.PyScript;

PyScriptExtensionBootstrap.Register();
```

运行时插件 DLL：`MDKOSS.Extensions.PyScript.dll`（由 `MdkPlugins.targets` 复制到 `plugins/`）。

## 目录

```text
src/MDKOSS.Extensions.PyScript/
├── MDKOSS.Extensions.PyScript.csproj
├── PyScriptExtension.cs          # IMdkExtension + Bootstrap + Actions
├── devices/
│   ├── pyscriptdev.cs
│   └── pyscript_device_parameters.cs
├── server/
│   └── api_pyscript_module.cs    # /api/pyscript/*
├── configs/
│   ├── pyscript.setting.json
│   └── scripts/hello.py
└── README.md
```

## 配置

```json
{
  "id": "py-1",
  "name": "Demo Python Script",
  "type": "devpyscript",
  "enabled": true,
  "parameters": {
    "pythonPath": "python",
    "scriptPath": "configs/scripts/hello.py",
    "workingDirectory": "",
    "arguments": "demo 1",
    "timeoutMs": "30000",
    "captureOutput": "true"
  }
}
```

| 参数 | 说明 | 默认 |
|------|------|------|
| `pythonPath` | Python 可执行文件路径或命令名 | `python` |
| `scriptPath` | 默认脚本路径（相对 `AppContext.BaseDirectory` 或绝对路径） | 空 |
| `workingDirectory` | 工作目录；空则使用脚本所在目录 | 空 |
| `arguments` | 传给脚本的额外参数（空格分隔，支持双引号） | 空 |
| `timeoutMs` | 超时毫秒；`0` 表示不超时 | `30000` |
| `captureOutput` | 是否捕获 stdout/stderr | `true` |

## 动作与 API

统一动作（`POST /api/devices/{id}/action`）：

| action | 说明 |
|--------|------|
| `run` / `execute` | 执行脚本；可选覆盖 `scriptPath` / `arguments` / `timeoutMs` |
| `kill` / `cancel` | 终止当前进程 |
| `status` / `result` | 状态与最近一次结果 |

REST：

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/pyscript/status?deviceId=` | 状态 |
| POST | `/api/pyscript/run` | body: `{ "deviceId", "scriptPath?", "arguments?", "timeoutMs?" }` |
| POST | `/api/pyscript/kill` | body: `{ "deviceId" }` |

Vars（前缀 `device.{name}.{id}.`）：`isRunning`、`lastExitCode`、`lastStdOut`、`lastStdErr`、`lastOk`、`lastDurationMs` 等。

## 注意

- 同一设备同一时刻只允许一个进程；并发 `run` 会返回 `already_running`。
- 需要本机已安装并可在 PATH（或 `pythonPath`）找到的 Python 解释器。
- 超时会 `Kill(entireProcessTree: true)`。
