# MDKOSS.Config.Wpf

WPF 离线配置工具：编辑 `*.setting.json`，并导出到 SQLite 配置表。

## 运行

```bash
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj
dotnet run --project src/MDKOSS.Config.Wpf/MDKOSS.Config.Wpf.csproj -- --setting configs/sample.setting.json
```

或 `run-config-wpf.bat`。

## 能力

| 操作 | 说明 |
|------|------|
| 打开 / 保存 JSON | 读写 `MdkSetting` |
| 导出到 SQLite | `MdkConfigStore.ExportSetting` → drivers/devices/gpios/axis/platform/positions/sysconfigs/recipes/logs/langs |
| 从 SQLite 导入 | 重建内存 `MdkSetting`（可再另存为 JSON） |

表结构见 [docs/data-persistence.md](../../docs/data-persistence.md)。WinForms 壳仍见 [MDKOSS.Config](../MDKOSS.Config/)。
