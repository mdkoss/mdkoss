# MDKOSS.Cef 界面分组

面向 `MonitoringServer` 的 HMI / 监控 / 调试 / 配置页面。命名统一为 **小写 + 下划线**。

## 分层原则

| 前缀 | 角色 | 写操作 | 典型用户 |
|------|------|--------|----------|
| `index.html` | 生产 HMI 主界面 | 仅启停/复位/选排单 | 操作员 |
| `popup_*.html` | index 内二级弹窗（简要查看/轻量选择） | 极少（如切换排单） | 操作员 |
| `monitor_*.html` | 详细状态只读监控 | 否 | 工艺/维护 |
| `debug_*.html` | 手动联调、点动、示教、维护工具 | 是 | 调试/维护 |
| `man_*.html` | 设备/组件配置管理（运行时侧） | 配置读写 | 工程师 |

机型扩展页（如 PNP 的 `indexPnp.html`）由 `StaticPageRegistry` 注册，不纳入核心改名。

`monitor_*` / `debug_*` / `man_*` 共用顶部工具栏 [`js/tool_nav.js`](js/tool_nav.js)（`.tool-chrome` 样式在 `css/debug.css`）：同组页面互相跳转，右侧切换「监控 / 调试 / 配置」，左侧回主界面。`index.html` 顶栏通过 iframe 打开 `popup_*.html?embedded=1`。

公共脚本：[`js/tool_common.js`](js/tool_common.js)（fetch/toast）、[`js/man_editor.js`](js/man_editor.js)（配置页 PATCH + save）。

## 主题

| 页面组 | 主题文件 | 风格 |
|--------|----------|------|
| `index` / `popup_*` | `css/theme-navy.css` + `css/main.css` | 深色 · 深蓝 |
| `monitor_*` / `debug_*` | `css/theme-gray.css` + `css/debug.css` | 浅色 · 灰色 |
| `man_*` | `css/theme-white.css` + `css/debug.css` | 白色 · 白底 |

主题文件只定义 CSS 变量与少量覆盖；控件结构统一在 `debug.css`（工具页）/ `main.css`（HMI）。

```
index ──popup_*──► monitor_* ──► debug_*
                 └──────────► man_*
```

## 现有文件映射

| 旧路径 | 新路径 |
|--------|--------|
| `index.html` | `index.html`（主壳；二级内容见 popup） |
| 顶栏下拉 / 排单 modal | `popup_*.html` |
| `monitoringpage.html` | `monitor_runtime.html` |
| `monitorIO.html` | `monitor_io.html` |
| `monitorPlatform.html` + `.js` | `debug_platform.html` + `debug_platform.js` |
| `debugserialdev.html` | `debug_serial.html` |
| `debugdb.html` | `debug_db.html` |
| `debugmachine.html` | `debug_machine.html` |
| `mandriver.html` | `man_driver.html` |

旧 URL 在 `MonitoringServer` 中保留别名，指向同一 HTML。

---

## 1. 主界面 `index.html`

| 区域 | 内容 |
|------|------|
| 顶栏 | 品牌/设备名、popup 入口、三色灯 |
| 主区 | 工单列表 |
| 底栏 | 当前工单、关键状态、当前排单、启动/停止/复位 |

顶栏入口 → popup：组件 / 任务 / 变量 / 报警 / 工单 / 用户 / 关于。弹窗通过遮罩 + iframe 加载 `popup_*.html?embedded=1`。

---

## 2. Popup 二级弹窗

| 页面 | 功能 | 数据 |
|------|------|------|
| `popup_devices.html` | 设备列表与状态；链到 monitor/debug | `GET /api/status` devices |
| `popup_tasks.html` | 任务状态摘要 | `task.cycle.*` / `task.operation.*` |
| `popup_vars.html` | 关键变量前 N 项（只读） | vars |
| `popup_alarms.html` | 报警/故障摘要 | devices + task vars |
| `popup_order.html` | 选中/当前工单详情 | `order.*` |
| `popup_recipe.html` | 排单列表与切换 | `/api/recipe` |
| `popup_user.html` | 用户/角色（占位） | 本地 |
| `popup_about.html` | 项目信息与工具页目录 | status + 链接 |

---

## 3. Monitor（只读）

| 页面 | 功能 |
|------|------|
| `monitor_runtime.html` | 运行时总览：驱动/设备/IO 摘要 |
| `monitor_io.html` | IO 点表详细监视 |
| `monitor_platform.html` | 平台轴位置/使能/故障（无点动） |
| `monitor_axis.html` | 单轴状态（骨架） |
| `monitor_camera.html` | 相机连接/取流状态（骨架） |
| `monitor_task.html` | 任务周期状态明细（骨架） |

风格：`css/debug.css`；标题带「只读」徽章。

---

## 4. Debug（可写）

| 页面 | 功能 |
|------|------|
| `debug_platform.html` | 平台步进示教/点动（见 `_docs/debug_platform.md`） |
| `debug_serial.html` | 串口收发调试 |
| `debug_db.html` | 数据库维护 |
| `debug_axis.html` | 单轴回零/点动（骨架） |
| `debug_camera.html` | 相机试调（骨架） |
| `debug_driver.html` | 驱动连接/试探（骨架） |
| `debug_io.html` | DO/VIO 强制输出（骨架） |
| `debug_machine.html` | 整机级手动（骨架） |

---

## 5. Man 配置管理

运行时轻量配置；完整编辑以 Config.Wpf 为主。

| 页面 | 功能 |
|------|------|
| `man_driver.html` | 驱动列表与参数摘要（骨架） |
| `man_device.html` | 设备与驱动绑定（骨架） |
| `man_axis.html` / `man_platform.html` | 轴/平台参数（骨架） |
| `man_gpio.html` | GPIO/VIO 点位别名（骨架） |
| `man_recipe.html` | 排单定义编辑（骨架） |
| `man_task.html` | 任务绑定配置（骨架） |

---

## 相关代码

- `src/MDKOSS.Core/server/monitoringserver.cs` — 路由与静态页
- `src/MDKOSS.Core/server/*page.cs` — HTML 加载器
- `src/MDKOSS.Cef/views/css/main.css` — 主界面
- `src/MDKOSS.Cef/views/css/debug.css` — 监控/调试工具页
