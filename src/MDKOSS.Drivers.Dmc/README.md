# MDKOSS.Drivers.Dmc

雷赛 LTDMC 运动控制卡 **原生 API 绑定**（`csLTDMC.LTDMC` / `LTDMC.dll`）。

当前尚未提供实现 `IDriver` 的 `DrvDmc` 包装类；需要时在本项目中新增 `DrvDmc : IDriver`，并在 `DmcDriverExtension.Register` 中：

```csharp
registration.Driver("dmc", () => new DrvDmc());
```

宿主按需注册：

```csharp
DmcDriverBootstrap.Register();
```

依赖方向：`MDKOSS.Drivers.Dmc → MDKOSS.Extensions → MDKOSS.Core`。
