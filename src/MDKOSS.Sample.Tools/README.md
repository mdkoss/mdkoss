# MDKOSS.Sample.Tools — 设备组件调试宿主

本工程把常见设备组件装进一份仿真配置，用来联调 `debug_*` / `monitor_*` / `man_*` 页。不做机型流程；驱动与扩展实现仍由 `plugins/` 提供。

## 1. 覆盖的组件

| 配置 ID | 类型 | 调试入口 |
|---------|------|----------|
| `sim1` | sim | `/debug_driver.html` |
| `drv-vio` / `vio1` | vio | `/debug_io.html` |
| `gpio1` | gpio | `/debug_io.html` |
| `axis-x` … `axis-u` | linear / rotary | `/debug_axis.html?deviceId=axis-x` |
| `platform-xy` / `xyz` / `xyzu` | 平台 | `/debug_platform.html?deviceId=platform-xyz` |
| `serial1` | serialdev | `/debug_serial.html`（不自动打开） |
| `tcp1` | tcpdev | `/debug_tcp.html`（不自动连接） |
| `mysql1` | mysqldev | `/debug_mysql.html`（不自动连接） |
| `mod-1` / `modc-1` | devmodserver / devmodclient | `/debug_modbus.html` |
| `cam1` / `cam-ext-1` | cameradev / extcamera | `/debug_camera.html` |
| `vision-1` | visiondev | `/debug_vision.html` |
| `py-1` | devpyscript | `/debug_pyscript.html`（`configs/scripts/hello.py`） |

```text
src/MDKOSS.Sample.Tools/
├── Tools/                     # 注册入口页
├── configs/sample.setting.json
├── configs/scripts/hello.py
└── views/index_tools.html     # 默认启动页
```

`Program.Main` 在插件发现后 `Register(new ToolsSampleExtension())`。

## 2. 运行

```bash
dotnet run --project src/MDKOSS.Sample.Tools/MDKOSS.Sample.Tools.csproj

dotnet run --project src/MDKOSS.Sample.Tools/MDKOSS.Sample.Tools.csproj -- --console
```

监控入口：`http://127.0.0.1:5088/index_tools.html`

工具页顶栏「主界面」回到本入口（`startPage` 覆盖了 `/` 与 `/index.html`）。

接真机：把对应设备的 `simulate` / 端口 / 主机改掉即可；运动与 IO 默认走 `sim`，无需板卡。

## 3. 打转发包

现场工程师不打开解决方案：解压 zip 后进 `http://127.0.0.1:5088/index_tools.html`，默认全仿真看 IO / 轴。

发布（Windows PowerShell，仓库根）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File src/MDKOSS.Sample.Tools/publish-tools.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File src/MDKOSS.Sample.Tools/verify-tools-pack.ps1
```

产物：`artifacts/MDKOSS.Tools-sim-win-x64.zip`（目录已 gitignore）。包内含 `开始.txt`、`plugins/`、`configs/`、`views/` 与 CEF 运行库。

收件人只需安装：

- .NET 8 **桌面**运行时（Desktop Runtime，框架依赖发布，**不**自包含整套 .NET）
- Microsoft Visual C++ 2019 x64（CEF 需要）

打开首页后优先 IO 页（`/debug_io.html`）与轴 / 平台页；完整组件表见上文「覆盖的组件」。CEF 随包分发，体积偏大属预期。

换机验收备忘见包内 `换机检查.txt`（对应第 4 周门禁：禁止开仓修代码，只允许装运行库）。

## 4. 标定

`type=flow` 且 `calib=true`、`autoStart=false`。用 [MDKOSS.Tools.Calib](../MDKOSS.Tools.Calib/README.md) 打开本配置执行；参数与结果写入 SQLite。

| 任务 | 流程 | 对象 |
|------|------|------|
| `calib-platform-xy` | `configs/flows/calib-platform-xy.flow.json` | `platform-xy` X |
| `calib-platform-z` | `configs/flows/calib-platform-z.flow.json` | `platform-xyz` Z |
