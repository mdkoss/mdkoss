# Camera 设备扩展（MDKOSS.Extensions.Camera）

与 `MDKOSS.Extensions` 同级的独立扩展程序集，用 **统一扩展接入接口**（`IMdkExtension` + `MdkExtensionHost`）接入 **市面常见面阵（area-scan）相机**。

与 Core 内置占位设备 `cameradev` 不同：本项目类型为 **`extcamera`**，带 open/trigger/status 与 `/api/extcamera` REST。

## 支持的相机

`backend` 参数从下表选一个 `type`（或别名）。**厂商 SDK 不随仓库分发**，需自行安装并保证运行时 DLL 在 `PATH` 或输出目录；DLL 缺失时按 `fallbackToSim` 回退到仿真，不会让运行时 fault。

| `backend` | 厂商 / 类别 | 运行时 DLL | 别名 |
|---|---|---|---|
| `sim` | 内置仿真（默认） | — | `simulate` `demo` `none` |
| `file` | 本地图像回放（离线调试） | — | `folder` `image` `replay` `offline` |
| `uvc` | 通用 USB 相机（OpenCV / DirectShow） | — | `usb` `opencv` `directshow` `webcam` |
| `hik` | 海康机器人 HikRobot MVS | `MvCameraControl.dll` | `hikvision` `hikrobot` `mvs` |
| `daheng` | 大恒图像 Galaxy | `GxIAPI.dll` | `galaxy` `gx` |
| `huaray` | 华睿科技 IMV | `MVSDKmd.dll` | `dahua` `imv` |
| `mindvision` | 迈德威视 | `MVCAMSDK_X64.dll` | `mindv` `mvcam` |
| `basler` | Basler pylon C | `PylonC.dll` | `pylon` |
| `flir` | Teledyne FLIR Spinnaker C | `SpinnakerC_v140.dll` | `spinnaker` `pointgrey` |
| `tis` | 映美精 The Imaging Source | `tisgrabber_x64.dll` | `imagingsource` `ic` |

SDK 版本不同会改 DLL 文件名（如 `PylonC_v9.dll`、`SpinnakerC_v140.dll`），用 `nativeDll` 参数覆盖即可，无需改代码。

`sim` / `file` / `uvc` 三个后端不依赖任何厂商 SDK，可直接跑通全流程。厂商后端的 P/Invoke 按各家公开手册编写，**上线前请在真机上验证一次**。

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
├── CameraExtension.cs              # IMdkExtension 实现 + Bootstrap
├── CameraCatalog.cs                # 相机型号族目录（backend 键）
├── NativeDllMap.cs                 # nativeDll 覆盖 [DllImport] 文件名
├── ExtCameraDeviceActions.cs
├── backends/
│   ├── camera_backend.cs           # 后端抽象 + CameraFrame + 工厂
│   ├── camera_pixel.cs             # PFNC 像素码 → BGR/Mono Mat
│   ├── builtin_camera_backends.cs  # sim / file / uvc
│   ├── native_hik_camera.cs
│   ├── native_daheng_camera.cs
│   ├── native_huaray_camera.cs
│   ├── native_mindvision_camera.cs
│   ├── native_basler_camera.cs
│   ├── native_spinnaker_camera.cs
│   └── native_tis_camera.cs
├── devices/
│   ├── extcameradev.cs
│   └── camera_device_parameters.cs
├── server/
│   └── api_extcamera_module.cs     # /api/extcamera/*
├── configs/
│   └── camera.setting.json
└── README.md
```

## 配置

```json
{
  "id": "cam-top",
  "name": "Downlook",
  "type": "extcamera",
  "enabled": true,
  "parameters": {
    "backend": "hik",
    "deviceIndex": "0",
    "serialNumber": "",
    "width": "2448",
    "height": "2048",
    "exposureUs": "8000",
    "gain": "2",
    "triggerMode": "software",
    "pixelFormat": "BayerRG8",
    "timeoutMs": "2000",
    "saveDir": "data/captures",
    "fallbackToSim": "true"
  }
}
```

| 参数 | 说明 | 默认 |
|------|------|------|
| `backend` | 上表的 `type` 或别名 | `sim` |
| `deviceIndex` | SDK 枚举序号（0 起） | `0` |
| `serialNumber` | 按序列号选相机，优先于 `deviceIndex`；迈德威视 / 映美精按「友好名」匹配 | 空 |
| `nativeDll` | 覆盖运行时 DLL 文件名 | 目录默认值 |
| `width` / `height` | ROI / 分辨率，`0` 表示保持相机当前值 | `1280` / `720` |
| `exposureUs` | 曝光（μs）；兼容旧的 `exposureMs` | `10000` |
| `gain` | 增益，`0` 表示不下发 | `0` |
| `triggerMode` | `continuous` / `software` / `hardware` | `continuous` |
| `pixelFormat` | GenICam 枚举符号（`Mono8` / `BGR8` / `BayerRG8`…），空则保持相机设置 | 空 |
| `timeoutMs` | 单次取图超时 | `2000` |
| `autoOpen` | 设备 Start 时自动打开 | `true` |
| `fallbackToSim` | 打开失败时降级为仿真 | `true` |
| `sourcePath` | `file` 后端的图片/目录；`uvc` 后端的流地址 | 空 |
| `saveDir` / `saveFormat` | 每次取图落盘的目录与格式（`png`/`jpg`/`bmp`/`tiff`） | 空 / `png` |
| `noisePx` | 仿真偏移噪声幅度 | `0.5` |

运行示例配置：

```bat
run-src-mdkoss.bat --setting configs\ext\camera.setting.json
```

（也可把 `configs/camera.setting.json` 复制到宿主输出目录的 `configs/`。）

## 与视觉联动

配置 `saveDir` 后，每次取图会落盘并写入变量 `device.{name}.{id}.lastImagePath`。`visiondev` 会先触发绑定的相机、再读这个变量作为流水线输入，因此 **相机 → 视觉** 只需：

```json
{ "type": "visiondev", "parameters": { "visionId": "vision-inspect", "cameraDeviceId": "cam-top" } }
```

## 动作与 API

统一动作（`POST /api/devices/{id}/action`）：

| action | 说明 |
|--------|------|
| `open` / `close` | 打开 / 关闭相机会话 |
| `trigger` / `capture` | 触发采集（可选参数 `recipe`） |
| `startgrab` / `stopgrab` | 起停取流 |
| `param` | 运行时调 `exposureUs` / `gain` / `triggerMode` |
| `list` | 枚举当前后端可见的相机 |
| `catalog` | 列出支持的相机型号族 |
| `status` / `result` | 状态与最近一次结果 |

REST：

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/extcamera/catalog` | 支持的相机型号族 |
| GET | `/api/extcamera/status?deviceId=` | 状态（含 `effectiveBackend`、`lastError`） |
| GET | `/api/extcamera/list?deviceId=` | 枚举该后端下的相机 |
| GET | `/api/extcamera/image?deviceId=&format=png` | 最近一帧图像（二进制） |
| POST | `/api/extcamera/open` | body: `{ "deviceId": "..." }` |
| POST | `/api/extcamera/close` | 同上 |
| POST | `/api/extcamera/startgrab` \| `/stopgrab` | 同上 |
| POST | `/api/extcamera/trigger` | body: `{ "deviceId": "...", "recipe": "..." }` |
| POST | `/api/extcamera/param` | body: `{ "deviceId": "...", "exposureUs": 8000, "gain": 2, "triggerMode": "software" }` |

## 如何再接一款相机

1. 在 `CameraCatalog` 加一条 `CameraKind`（`type` / 厂商 / 默认 DLL / 别名）
2. 在 `backends/` 新增 `CameraBackend` 子类，实现 `TryOpen` / `Close` / `TryGrab`，按需覆写曝光、增益、触发
3. 在 `CameraBackend.Create` 的 switch 里挂上新 `type`

抓到的帧只需带上 GenICam PFNC 像素码，`CameraPixel` 会统一解码为 BGR/Mono；若 SDK 已输出 BGR8/Mono8，直接填对应常量即可。

## 如何写下一个设备扩展

1. 在 `src/` 下新建与 `MDKOSS.Extensions` 同级的类库，引用 `MDKOSS.Extensions`
2. 实现 `IMdkExtension`，在 `Register(IExtensionRegistration r)` 中调用：
   - `r.Device(...)` / `r.Action(...)` / `r.MonitoringModule(...)`
   - 可选：`r.Task` / `r.Driver` / `r.StaticPage`
3. 宿主 `MdkExtensionHost.Register(new YourExtension())`

详见 [docs/extensions.md](../../docs/extensions.md)。
