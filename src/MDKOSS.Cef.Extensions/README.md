# MDKOSS.Cef.Extensions — 主界面监控组态

扩展 CEF 主界面，用 JSON 画布置放监控控件。不引用 CefSharp；经 `IMdkExtension` 注册页面与 API，由宿主 `DiscoverAndRegister` 从 `plugins/` 加载。

控件目录是**注册表**，不是写死在 C# / JS 里的 switch。内置类型与第三方一样，从扩展包载入。

## 如何加控件（不用改核心代码）

每种控件一个文件夹：

```
views/widgets/{type}/
  widget.json   # type / displayName / category / defaultW/H / props
  widget.js     # MdkHmi.register(type, { create, update })
  widget.css    # 可选
```

放到下列任一位置即可出现在调色板与运行页：

| 位置 | 用途 |
|------|------|
| 宿主 `views/widgets/{type}/` | 最简单：拷文件夹 |
| `plugins/{包名}/widgets/{type}/` | 随插件分发 |
| `IHmiWidgetPackage` 或 `HmiWidgetRegistry.Register` | DLL 包（可内嵌 JS） |

程序集命名 `MDKOSS.Cef.Extensions.*.dll` 或 `MDKOSS.Extensions.*.dll` 会被 `DiscoverAndRegister` 加载。

`widget.js` 示例：

```javascript
MdkHmi.register("gauge", {
  create(el, widget, ctx) { el.textContent = "—"; },
  update(el, widget, vars, ctx) {
    el.textContent = String(MdkHmi.varVal(vars, MdkHmi.prop(widget, "var", "")) ?? "—");
  },
});
```

## 内置控件（`hmi-builtin`）

同样位于 `views/widgets/`：

| type | 作用 | 主要属性 |
|------|------|----------|
| `label` | 静态文本 | `text` / `align` / `fontSize` |
| `value` | 绑定变量显示 | `var` / `label` / `unit` |
| `lamp` | 指示灯 | `var`（`red`/`yellow`/`green` 或布尔） |
| `progress` | 进度条 | `var` / `min` / `max` |
| `status` | 状态灯 | `var` / `okWhen`=`truthy\|falsy\|zero\|equals` |
| `button` | 调用监控 API | `text` / `method` / `url` / `style` |

## 页面与 API

| 路径 | 说明 |
|------|------|
| `/index_hmi.html` | 运行页（可作 `startPage`） |
| `/man_hmi.html` | 组态编辑（拖放 / 属性 / 保存） |
| `GET /api/hmi/layout` | 当前布局 |
| `PUT /api/hmi/layout` | 保存到 `hmi.layout.json` |
| `GET /api/hmi/widgets` | 已注册控件（含 `script` / `css`） |
| `GET /api/hmi/widget/{type}.js` | 控件脚本 |
| `GET /api/hmi/widget/{type}.css` | 控件样式 |
| `POST /api/hmi/reset` | 恢复默认布局 |

布局文件写在 setting 同目录的 `hmi.layout.json`；没有 setting 路径时用 `configs/hmi.layout.json`。

`startPage` 设为 `index_hmi.html` 即用组态页作为主界面（`MDKOSS.Cef.Sample` 已这样配）。默认 `index.html` 顶栏在扩展已加载时显示「组态」入口。
