# MDKOSS.Cef.Sample

轻量 CEF 宿主，专门打开 [`MDKOSS.Cef/views/index.html`](../MDKOSS.Cef/views/index.html) 联调核心 HMI：

- `popup_*` 二级弹窗  
- `monitor_*` 监控  
- `debug_*` 调试  
- `man_*` 配置  

不加载 DieBonder / PNP 机型页。

**views 复用**：本项目不复制/不 Link `MDKOSS.Cef/views`；通过 `ProjectReference` 引用 `MDKOSS.Cef`，构建时 Content 直接落到输出目录的 `views/`（避免再 Link 造成 `views\views` 嵌套）。

## 运行

```bash
dotnet run --project src/MDKOSS.Cef.Sample/MDKOSS.Cef.Sample.csproj -c Debug
```

或 Visual Studio 将 `MDKOSS.Cef.Sample` 设为启动项目。

默认监控地址：`http://127.0.0.1:5081/`（与 DieBonder Sample 的 5080 错开）。

浏览器也可直接访问同一前缀下的页面，例如：

- http://127.0.0.1:5081/index.html  
- http://127.0.0.1:5081/monitor_runtime.html  
- http://127.0.0.1:5081/debug_axis.html  

## 配置

- [`configs/sample.setting.json`](configs/sample.setting.json)：仿真驱动 + GPIO / 轴 / XY 平台 / 相机 + 演示订单与排单  
- 可用 `--setting path\to\file.json` 覆盖  

## 依赖

- `MDKOSS.Core` / `MDKOSS.Extensions` / `MDKOSS.Cef`  
- 插件：`MDKOSS.Drivers.Sim`、`Extensions.Serial`、`Extensions.Camera`（构建时复制到 `plugins/`）  
