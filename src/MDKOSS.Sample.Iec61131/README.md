# MDKOSS.Sample.Iec61131 — 工位节拍 → IEC 61131

把一个基于框架 **Flow 任务** 的工位程序导出为可在 TIA Portal / CODESYS 里打开的 IEC 61131-3 工程。

## 业务

等启动按钮 → 检查安全门 / 停止 → 使能轴与平台 → 取位 → 夹爪 → 放位 → 有工件则开阀 → 计件。停止走 `FaultHold` 子 FB。

## 运行

```bash
dotnet run --project src/MDKOSS.Sample.Iec61131/MDKOSS.Sample.Iec61131.csproj
```

写入：

- `configs/station.setting.json` — MDKOSS 工程（GPIO / 轴 / `type=flow`）
- `configs/station.flow.json` — Flow 节点图
- `export/` — 转换结果（`plcopen.xml`、`scl/*.scl`、`mapping.json`）

在 TIA 中导入 `export/plcopen.xml`，或把 `export/scl/` 下 SCL 拷进程序块。把 `PROGRAM_Main` 挂到周期 OB（默认 20ms）。
