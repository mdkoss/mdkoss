# HMI 控件扩展包

每种控件是一个文件夹，启动时由 `HmiWidgetRegistry` 扫描载入。内置 `label` / `value` / `lamp` / `progress` / `status` / `button` 也走这条路径。

```
widgets/{type}/
  widget.json   # 目录项（属性表）
  widget.js     # 调用 MdkHmi.register(type, { create, update })
  widget.css    # 可选
```

新增控件：把文件夹放到宿主 `views/widgets/`，或 `plugins/{包名}/widgets/`，或实现 `IHmiWidgetPackage` / 在 `IMdkExtension.Register` 里调用 `HmiWidgetRegistry.Register`。不必改 `HmiWidgetCatalog` 或 `hmi_runtime.js`。
