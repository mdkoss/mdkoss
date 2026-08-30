# src/ Agent

你在 **MDKOSS** 运行时内核上工作。这是从 `mdkruntime` 提炼的开源简化运行时：配置驱动、可编译、可运行、可观测、可扩展。

工作区可以是仓库根；**只改 `src/`**。必要测试可改 `tests/`。不要改 `android/`、`scripts/`、密钥、`.gitignore` 里的本地目录。

详细架构以仓库 `docs/` 为准，尤其是 [project-layout.md](../docs/project-layout.md)、[extensions.md](../docs/extensions.md)、[core-subsystems.md](../docs/core-subsystems.md)。

## 分层与依赖

```
宿主（Sample / Cef.Sample / UI.WPF / Sample.DieBonder / Sample.Dispenser / Sample.Pnp / Sample.Tools / Config.Wpf）
  → Core + Extensions
      ↑
Drivers.* / Extensions.*  （插件 DLL，运行时扫 plugins/）
```

- **Core 永不引用扩展或板卡实现。**
- 新通信/外设能力做成独立 `MDKOSS.Extensions.*`，通过 `IMdkExtension` 注册，不要塞进 Core。
- 新板卡做成 `MDKOSS.Drivers.*`，实现 `IDriver`，用 Bootstrap 注册 `type`。

| 项目 | 职责 |
|------|------|
| `MDKOSS.Core` | `MdkRuntime`、`IDriver`、设备基类、任务调度、`MVarStore`、监控 HTTP/API、SQLite |
| `MDKOSS.Extensions` | 扩展接入层（注册表 / Host） |
| `MDKOSS.Drivers.Sim` / `Gts` / `Dmc` / `S7` | 仿真 / 固高 / 雷赛 / 西门子 S7-1200 |
| `MDKOSS.Extensions.Serial` / `Tcp` / `Mysql` / `Camera` / `PyScript` / `ModServer` | 可选设备（ModServer 另注册 `modbus` IDriver） |
| `MDKOSS.Cef` | CefSharp 壳 + `views/` HMI |
| `MDKOSS.Cef.Extensions` | 主界面监控组态（`index_hmi` / `man_hmi` / `/api/hmi`；控件见 `views/widgets`） |
| `MDKOSS.Cef.Sample` | 加载并运行 `configs/` 下第一个 JSON 的 CEF 宿主 |
| `MDKOSS.UI.WPF` | Prism + WPF 主界面宿主（`index.html` 对应的操作员 HMI） |
| `MDKOSS.Sample` | SampleExt 扩展示例宿主 |
| `MDKOSS.Sample.DieBonder` | 半导体贴片机 Demo 宿主 |
| `MDKOSS.Sample.Dispenser` | 三轴点胶机 Demo 宿主 |
| `MDKOSS.Sample.Pnp` | 拾取放置（PNP）Demo 宿主 |
| `MDKOSS.Sample.Modbus` | Modbus IDriver 联调宿主（默认 200 Holding Register） |
| `MDKOSS.Sample.Tools` | 设备组件调试宿主（轴 / IO / 串口 / TCP / 相机 / 视觉 / MySQL 等） |
| `MDKOSS.Config.Wpf` | 离线配置编辑器 |
| `MDKOSS.Iec61131` | Flow 任务 / 变量 / GPIO 导出为 IEC 61131-3 |
| `MDKOSS.Sample.Iec61131` | 工位节拍 IEC 导出示例 |

Issue 的 `module` 对照：`axis`→轴/平台，`gpio`→IO，`vision`→视觉/相机，`recipe`→配方（参数组；排单指生产工单），`driver`→`IDriver`/板卡，`other`→先搜再改。

## 改哪里

| 问题类型 | 优先看 |
|----------|--------|
| 轴/点动/回零/超差 | `Core/core` 轴与平台参数；`Drivers.*` |
| DI/DO | `gpio_device_parameters`、驱动 IO 地址 |
| 串口/TCP/MySQL | 对应 `Extensions.*`，不要改 Core 生命周期去硬接协议 |
| 视觉/相机 | `Core/vision`、`Extensions.Camera` |
| 任务/流程 | `Core/tasks`、`Core/tasks/flow` |
| 监控 API / HMI | `Core/server`、`Cef/views`、`Cef.Extensions`（主界面组态）、`UI.WPF`（Prism 主界面） |
| 配置 JSON / 编辑器 | `MdkSetting`、`Config.Wpf`（HMI 组态在系统组 `Hmi`） |
| 配方/工单 | `Core/core/data`、`api_orders_module` |

`Cef/views` 命名：小写+下划线。`index` / `popup_*` / `monitor_*` / `debug_*` / `man_*` 分层不要混用。公共脚本用已有的 `tool_common.js` / `tool_nav.js` / `man_editor.js`。

## 代码约定

- 跟随周围文件的命名、分层和注释密度；不要顺手大重构。
- 只在逻辑不直观处加简短注释。
- 设备动作走已有 `DeviceActionRegistry` / 扩展注册，避免在 Runtime 里堆 `switch`。
- 配置字段与 `*.setting.json`、参数类、监控页三者对齐。
- 提交说明面向人类，1–2 句写原因；不要署名 Cursor / AI / agent。
- **不要自行 `git push`。** Issue 由轮询脚本在处理后推送。未要求时不要改 git config、不要 force。

## 验证

能跑的测试要跑。优先缩小范围：

```bash
dotnet test tests/MDKOSS.Tests/MDKOSS.Tests.csproj -c Debug --filter FullyQualifiedName~<相关类或命名空间>
```

全量（较慢）：

```bash
dotnet test MDKOSS.sln -c Release
```

改 HMI 时至少确认对应 `views/*.html` 与 `Core/server` 路由/API 仍对得上。改驱动时用 `sim` 能测的部分先测，不要假设现场板卡在本机。

## 处理 Issue 时

收到带 Issue id / 标题 / 描述的消息后**立刻动手改代码**，不要先回复「已就位 / 待命 / 直接说要处理的 issue」。那条消息本身就是任务。

1. 读标题、描述、全部评论、`module` / `type` / `priority`，先在上表定位，再搜符号。后写的评论优先。
2. 最小改动修复；补或改测试证明行为。
3. `git add` 只加相关文件并 commit。不要 push，轮询脚本会紧接着推送。
4. 用纯文本写 3–8 句方案摘要（改了什么、为什么、怎么验证）。不要 markdown 标题，不要双引号。
