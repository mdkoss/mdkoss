# MDKOSS 文档

本目录集中存放 **架构设计** 与 **UI 设计参考**，与根目录 [readme.md](../readme.md) 中的快速上手、构建运行说明互补。

## 文档索引

| 文档 | 说明 |
|------|------|
| [architecture.md](./architecture.md) | 总体架构：分层、生命周期、数据流、与 mdkruntime 的关系 |
| [project-layout.md](./project-layout.md) | 解决方案结构、项目拆分、目录与模块职责 |
| [configuration.md](./configuration.md) | JSON 配置模型（drivers / devices / axes / platforms / tasks / vars / recipes） |
| [core-subsystems.md](./core-subsystems.md) | 驱动、设备、任务调度、变量中心、配方管理 |
| [extensions.md](./extensions.md) | MDKOSS.Extensions 扩展机制与注册表 |
| [monitoring-api.md](./monitoring-api.md) | HTTP 监控服务、静态 HMI 页面、REST API |
| [data-persistence.md](./data-persistence.md) | SQLite 持久化：排单、示教点、配方同步 |
| [issues.md](./issues.md) | Issue 提交管理：mdkossdb 表结构 + Android 直连应用 |
| [gui.md](./gui.md) | WPF / CEF 桌面壳与配置管理工具 |
| [winform-epson-rc-design.md](./winform-epson-rc-design.md) | 历史 WinForms 配置界面设计参考（EPSON RC+ 风格） |
| [MDKOSS.Config.Wpf/design.md](../src/MDKOSS.Config.Wpf/design.md) | WPF 配置界面设计指引（实现约定） |

## 阅读顺序建议

1. **新人 onboarding**：architecture → project-layout → configuration → monitoring-api  
2. **扩展新设备类型**：extensions → core-subsystems（设备层）→ monitoring-api  
3. **改配置 UI**：gui → [MDKOSS.Config.Wpf/design.md](../src/MDKOSS.Config.Wpf/design.md)  

## 相关资源

- 示例配置：`src/MDKOSS.Config.Wpf/configs/sample.setting.json`、`src/MDKOSS.Sample/configs/sample.setting.json`
- 配置模块说明：`src/MDKOSS.Config.Wpf/README.md`、`src/MDKOSS.Config.Wpf/design.md`
- 设备扩展说明：`src/MDKOSS.Extensions.Serial/serialdev.md`、`src/MDKOSS.Extensions.Tcp/tcpdev.md`、`src/MDKOSS.Extensions.Mysql/mysqldev.md`
- 平台示教页设计：`src/MDKOSS.Cef/views/_docs/debug_platform.md`
- 界面分组：`src/MDKOSS.Cef/views/README.md`
- 单元测试：`tests/MDKOSS.Tests/`
