# MDKOSS Release Notes

## v0.1.0 - Initial Open Source Release (2026-04-22)

This is the first public release of MDKOSS.

### Highlights

- Open-source baseline runtime extracted from `MDKSYS/mdkruntime`.
- Config-driven runtime bootstrap from JSON settings.
- Driver abstraction with `IDriver` and sample `DrvGts` implementation.
- Device model support for `gpio`, `axis`, `platform`, and `cameradev`.
- Task scheduler and heartbeat update pipeline.
- Runtime state store with monitoring snapshot export.
- Lightweight monitoring endpoint and dashboard page.

### Open Source Governance

- License: MIT (`LICENSE`)
- Contribution guide: `CONTRIBUTING.md`
- Community conduct: `CODE_OF_CONDUCT.md`
- Security disclosure process: `SECURITY.md`

---

## v0.1.0 - 初始开源版本（2026-04-22）

这是 MDKOSS 的首个公开发布版本。

### 版本要点

- 从 `MDKSYS/mdkruntime` 提炼出的开源基础运行时。
- 支持基于 JSON 配置的运行时初始化。
- 提供驱动抽象接口 `IDriver` 与示例驱动 `DrvGts`。
- 支持 `gpio`、`axis`、`platform`、`cameradev` 设备模型。
- 提供任务调度与心跳更新机制。
- 提供运行时状态中心与监控快照导出能力。
- 提供轻量监控接口和仪表盘页面。

### 开源治理

- 许可证：MIT（`LICENSE`）
- 贡献指南：`CONTRIBUTING.md`
- 社区行为准则：`CODE_OF_CONDUCT.md`
- 安全漏洞披露流程：`SECURITY.md`
