# Camera 设备扩展（MDKOSS.Extensions.Camera）

与 `MDKOSS.Extensions` 同级的独立扩展程序集，演示如何用 **统一扩展接入接口**（`IMdkExtension` + `MdkExtensionHost`）开发设备扩展 DLL。

与 Core 内置占位设备 `cameradev` 不同：本项目类型为 **`extcamera`**，带 open/trigger/status 与 `/api/extcamera` REST。

## 接入方式

宿主在 `new MdkRuntime` **之前**：

```csharp
using MDKOSS.Extensions.Serial;
using MDKOSS.Extensions.Tcp;
using MDKOSS.Extensions.Camera;

SerialExtensionBootstrap.Register();
TcpExtensionBootstrap.Register();
CameraExtensionBootstrap.Register();
```

项目引用：

```xml
<ProjectReference Include="..\MDKOSS.Extensions.Camera\MDKOSS.Extensions.Camera.csproj" />
```

## 目录

```text
src/MDKOSS.Extensions.Camera/
├── MDKOSS.Extensions.Camera.csproj
├── CameraExtension.cs          # IMdkExtension 实现 + Bootstrap
├── ExtCameraDeviceActions.cs
├── devices/
│   ├── extcameradev.cs
│   └── camera_device_parameters.cs
├── server/
│   └── api_extcamera_module.cs # /api/extcamera/*
├── configs/
│   └── camera.setting.json
└── README.md
```

## 配置

```json
{
  "id": "cam-ext-1",
  "name": "Demo Ext Camera",
  "type": "extcamera",
  "enabled": true,
  "parameters": {
    "backend": "sim",
    "deviceIndex": "0",
    "width": "1280",
    "height": "720",
    "exposureMs": "12",
    "noisePx": "0.8"
  }
}
```

| 参数 | 说明 | 默认 |
|------|------|------|
| `backend` | 后端标记（示例仅实现 `sim`） | `sim` |
| `deviceIndex` | 设备序号 | `0` |
| `width` / `height` | 分辨率 | `1280` / `720` |
| `exposureMs` | 曝光（ms，仿真占位） | `10` |
| `noisePx` | 仿真偏移噪声幅度 | `0.5` |

运行示例配置：

```bat
run-src-mdkoss.bat --setting configs\camera.setting.json
```

（也可把 `configs/camera.setting.json` 复制到宿主输出目录的 `configs/`。）

## 动作与 API

统一动作（`POST /api/devices/{id}/action`）：

| action | 说明 |
|--------|------|
| `open` / `close` | 打开 / 关闭相机会话 |
| `trigger` / `capture` | 触发采集（可选参数 `recipe`） |
| `status` / `result` | 状态与最近一次结果 |

REST：

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/extcamera/status?deviceId=` | 状态 |
| POST | `/api/extcamera/open` | body: `{ "deviceId": "..." }` |
| POST | `/api/extcamera/close` | 同上 |
| POST | `/api/extcamera/trigger` | body: `{ "deviceId": "...", "recipe": "..." }` |

## 如何写下一个设备扩展

1. 在 `src/` 下新建与 `MDKOSS.Extensions` 同级的类库，引用 `MDKOSS.Extensions`
2. 实现 `IMdkExtension`，在 `Register(IExtensionRegistration r)` 中调用：
   - `r.Device(...)` / `r.Action(...)` / `r.MonitoringModule(...)`
   - 可选：`r.Task` / `r.Driver` / `r.StaticPage`
3. 宿主 `MdkExtensionHost.Register(new YourExtension())`

详见 [docs/extensions.md](../../docs/extensions.md)。
