# MDKOSS.Sample — 扩展示例宿主

本工程演示如何在宿主内注册自定义设备 / MotionTask / API / 页面（`SampleExt/`）。机型场景见独立工程：`MDKOSS.Sample.DieBonder`、`MDKOSS.Sample.Dispenser`。

## 1. SampleExt

```text
src/MDKOSS.Sample/
├── SampleExt/
│   ├── SampleExtExtension.cs
│   ├── SampleBeaconDevice.cs   # type=samplebeacon
│   ├── SampleMotionDemoTask.cs # type=samplemotion（enable/move/jog/stop）
│   ├── SampleExtApiModule.cs   # /api/sampleext/*
│   └── SampleExtViewPages.cs
└── views/
    └── demo_sample_ext.html
```

| 能力 | 路径 |
|------|------|
| 扩展示例页 | `/demo_sample_ext.html`（配置 `startPage`） |
| 系统主界面 | `/index.html`（Cef） |
| SampleExt 状态 | `GET /api/sampleext/status` |
| SampleExt 动作 | `POST /api/sampleext/pulse\|motionstart\|motionstop\|reset` |

`Program.Main` 在插件发现后 `Register(new SampleExtExtension())`。

配置条目：`sample-beacon`（设备）与 `sample-motion-demo`（任务）。

## 2. 运行

```bash
dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj

dotnet run --project src/MDKOSS.Sample/MDKOSS.Sample.csproj -- --console
```

监控入口：`http://127.0.0.1:5083/demo_sample_ext.html`
