# 项目结构与模块职责

## 仓库根目录

```text
mdkoss/
├── MDKOSS.sln
├── readme.md                 # 快速上手、构建运行（指向 docs/ 获取架构细节）
├── docs/                     # 架构与设计文档（本目录）
├── build-src-mdkoss.bat      # 构建脚本
├── run-src-mdkoss*.bat       # 运行脚本
├── src/                      # 主源码
└── tests/MDKOSS.Tests/       # xUnit 测试
```

## 解决方案项目

### MDKOSS（可执行程序）

- **路径**：`src/MDKOSS.csproj`
- **输出**：`win-x64` 可执行文件
- **依赖**：`MDKOSS.Core`、`MDKOSS.Extensions`、CefSharp、NLog
- **编译范围**：`Program.cs`、`gui/**/*.cs`
- **复制到输出目录**：`configs/**`、`views/**`

### MDKOSS.Core（运行时内核）

- **路径**：`src/MDKOSS.Core.csproj`
- **程序集名**：`MDKOSS.Core.dll`
- **编译范围**：
  - `core/**/*.cs`
  - `server/**/*.cs`（HTTP 监控服务与 API 模块）
  - `tasks/**/*.cs`

### MDKOSS.Extensions（可选扩展）

- **路径**：`src/extensions/MDKOSS.Extensions.csproj`
- **程序集名**：`MDKOSS.Extensions.dll`
- **内容**：`serialdev`、`tcpdev` 设备实现及对应 HTTP API 模块
- **依赖**：`System.IO.Ports`、Core 项目引用

## src/ 目录详解

```text
src/
├── Program.cs                    # 入口：ExtensionsBootstrap → UI 模式 → MdkRuntime
├── configs/
│   └── sample.setting.json       # 示例工程配置
├── views/                        # HMI 静态页（构建时复制到输出目录）
│   ├── index.html                # 监控首页导航
│   ├── monitoringpage.html       # 综合监控
│   ├── monitorIO.html            # IO 监控
│   ├── monitorPlatform.html      # 平台步进示教
│   └── debugSerialDev.html       # 串口调试
├── tasks/                        # 内置任务实现
│   ├── task_operation.cs
│   ├── task_cycle.cs
│   └── task_motion.cs            # MotionTask / TaskMotionTask（运动与 GPIO 设备封装）
├── core/                         # 内核（MDKOSS.Core）
│   ├── mdk.cs                    # MdkRuntime 宿主
│   ├── msetting.cs               # MdkSetting 配置模型
│   ├── mdev.cs                   # 设备基类与 Core 内置设备
│   ├── mtask.cs                  # 任务基类与 MTaskScheduler
│   ├── mvar.cs                   # MVarStore 变量中心
│   ├── mrecipe.cs                # MdkRecipeManager 配方
│   ├── mdk_errors.cs             # 错误码
│   ├── app_log.cs                # NLog 封装
│   ├── runtime_task_factory.cs   # 任务类型注册表
│   ├── device_extension_registry.cs
│   ├── device_action_registry.cs
│   ├── gpio_device_parameters.cs
│   ├── vio_device_parameters.cs
│   ├── platform_device_parameters.cs
│   ├── drivers/
│   │   ├── idriver.cs
│   │   ├── driver_factory.cs     # gts / sim 内置驱动
│   │   ├── drvgts.cs
│   │   ├── drvsim.cs
│   │   └── drvdmc.cs             # DMC 驱动实现（按需注册）
│   └── data/
│       ├── mdk_database.cs
│       ├── mdk_data_store.cs
│       └── mdk_data_models.cs
├── server/                       # HTTP 监控服务（编入 MDKOSS.Core）
│   ├── monitoringserver.cs
│   ├── monitoring_api_module.cs
│   ├── monitoring_module_registry.cs
│   ├── api_status_module.cs
│   ├── api_io_module.cs
│   ├── api_devices_module.cs
│   ├── api_recipe_module.cs
│   ├── api_orders_module.cs
│   ├── api_teach_module.cs
│   ├── api_task_module.cs
│   ├── api_db_module.cs
│   └── *page.cs                  # 静态 HTML 加载器
├── extensions/                   # MDKOSS.Extensions
│   ├── ExtensionsBootstrap.cs    # 统一注册入口
│   ├── serialdev.cs / tcpdev.cs
│   ├── api_serial_module.cs / api_tcp_module.cs
│   └── *device_parameters.cs
└── gui/
    ├── winform/                  # WinForms 监控与配置
    │   ├── MainForm.cs
    │   ├── ComponentConfigForm.cs
    │   ├── DeviceManagerForm.cs
    │   ├── TaskManagerForm.cs
    │   ├── IoMonitorForm.cs
    │   └── *ConfigForm.cs        # 各子系统配置页
    └── cef/
        ├── CefMainForm.cs
        └── CefRuntimeBootstrap.cs
```

## 关键类型与文件映射

| 关注点 | 主要文件 |
|--------|----------|
| 应用入口 | `Program.cs` |
| 运行时宿主 | `core/mdk.cs` |
| 配置加载 | `core/msetting.cs` |
| 驱动工厂 | `core/drivers/driver_factory.cs` |
| 设备体系 | `core/mdev.cs` + `device_extension_registry.cs` |
| 任务调度 | `core/mtask.cs`, `core/runtime_task_factory.cs`, `tasks/` |
| 变量与配方 | `core/mvar.cs`, `core/mrecipe.cs` |
| 持久化 | `core/data/mdk_data_store.cs` |
| HTTP 监控 | `server/monitoringserver.cs` |
| 扩展注册 | `extensions/ExtensionsBootstrap.cs` |

## 构建产物布局

Debug/Release 构建后，典型输出目录：

```text
src/bin/{Configuration}/net8.0-windows10.0.22621.0/win-x64/
├── MDKOSS.exe
├── MDKOSS.Core.dll
├── MDKOSS.Extensions.dll
├── configs/sample.setting.json
├── views/*.html
├── data/mdk.db                   # 运行时创建
└── logs/yyyyMMdd.log
```

运行时默认从 **可执行文件同目录** 加载 `configs/sample.setting.json`，数据库默认 `data/mdk.db`。

## 测试项目

`tests/MDKOSS.Tests/` 覆盖配置解析、配方 API、设备行为等。与 Core 同目标框架，引用主工程输出进行集成测试。

## 延伸阅读

- [architecture.md](./architecture.md) — 分层与生命周期
- [extensions.md](./extensions.md) — 为何拆分 Extensions 项目
- [configuration.md](./configuration.md) — JSON 字段说明
