/**
 * Unified tool chrome for monitor_* / debug_* / man_* pages.
 * Auto-detects from path or body[data-tool-group][data-tool-page].
 */
(function () {
  const GROUPS = {
    monitor: {
      label: "监控",
      pages: [
        { id: "monitor_runtime", href: "/monitor_runtime.html", label: "总览" },
        { id: "monitor_io", href: "/monitor_io.html", label: "IO" },
        { id: "monitor_platform", href: "/monitor_platform.html", label: "平台" },
        { id: "monitor_axis", href: "/monitor_axis.html", label: "轴" },
        { id: "monitor_camera", href: "/monitor_camera.html", label: "相机" },
        { id: "monitor_vision", href: "/monitor_vision.html", label: "视觉" },
        { id: "monitor_task", href: "/monitor_task.html", label: "任务" },
        { id: "monitor_alarm", href: "/monitor_alarm.html", label: "报警" },
      ],
    },
    debug: {
      label: "调试",
      pages: [
        { id: "debug_platform", href: "/debug_platform.html", label: "平台示教" },
        { id: "debug_serial", href: "/debug_serial.html", label: "串口" },
        { id: "debug_mysql", href: "/debug_mysql.html", label: "MySQL" },
        { id: "debug_axis", href: "/debug_axis.html", label: "轴" },
        { id: "debug_io", href: "/debug_io.html", label: "IO 强制" },
        { id: "debug_camera", href: "/debug_camera.html", label: "相机" },
        { id: "debug_vision", href: "/debug_vision.html", label: "视觉" },
        { id: "debug_driver", href: "/debug_driver.html", label: "驱动" },
        { id: "debug_db", href: "/debug_db.html", label: "数据库" },
        { id: "debug_machine", href: "/debug_machine.html", label: "整机" },
        { id: "debug_alarm", href: "/debug_alarm.html", label: "报警" },
      ],
    },
    man: {
      label: "配置",
      pages: [
        { id: "man_machine", href: "/man_machine.html", label: "整机" },
        { id: "man_driver", href: "/man_driver.html", label: "驱动" },
        { id: "man_device", href: "/man_device.html", label: "设备" },
        { id: "man_axis", href: "/man_axis.html", label: "轴" },
        { id: "man_platform", href: "/man_platform.html", label: "平台" },
        { id: "man_gpio", href: "/man_gpio.html", label: "GPIO" },
        { id: "man_task", href: "/man_task.html", label: "任务" },
        { id: "man_vars", href: "/man_vars.html", label: "变量" },
        { id: "man_recipe", href: "/man_recipe.html", label: "配方" },
        { id: "man_vision", href: "/man_vision.html", label: "视觉" },
        { id: "man_alarm", href: "/man_alarm.html", label: "报警" },
        { id: "man_hmi", href: "/man_hmi.html", label: "主界面组态" },
      ],
    },
  };

  const CROSS = [
    { group: "monitor", href: "/monitor_runtime.html", label: "监控" },
    { group: "debug", href: "/debug_platform.html", label: "调试" },
    { group: "man", href: "/man_machine.html", label: "配置" },
  ];

  function detect() {
    const body = document.body;
    let group = (body.getAttribute("data-tool-group") || "").toLowerCase();
    let page = (body.getAttribute("data-tool-page") || "").toLowerCase();
    if (!group || !page) {
      const path = (location.pathname || "").split("/").pop() || "";
      const m = path.match(/^(monitor|debug|man)_([a-z0-9_]+)\.html$/i);
      if (m) {
        group = m[1].toLowerCase();
        page = (m[1] + "_" + m[2]).toLowerCase();
      }
    }
    return { group, page };
  }

  function preserveQuery(href) {
    const id = new URLSearchParams(location.search).get("deviceId");
    if (!id) return href;
    const u = new URL(href, location.origin);
    if (!u.searchParams.has("deviceId")) u.searchParams.set("deviceId", id);
    return u.pathname + u.search;
  }

  function render() {
    const { group, page } = detect();
    const cfg = GROUPS[group];
    if (!cfg) return;

    document.body.setAttribute("data-tool-group", group);
    document.body.setAttribute("data-tool-page", page);
    document.body.setAttribute("data-theme", group === "man" ? "white" : "gray");

    // Remove ad-hoc page navs to avoid duplicates
    document.querySelectorAll(".header nav.nav, .header .nav").forEach((n) => n.remove());

    if (document.getElementById("toolChrome")) return;

    const bar = document.createElement("div");
    bar.id = "toolChrome";
    bar.className = "tool-chrome";
    bar.innerHTML =
      '<div class="tool-chrome-inner">' +
      '<a class="tool-home" href="/">主界面</a>' +
      '<span class="tool-group-label">' + cfg.label + "</span>" +
      '<nav class="tool-pages" aria-label="' + cfg.label + '页面">' +
      cfg.pages
        .map((p) => {
          const active = p.id === page ? " active" : "";
          return (
            '<a class="tool-page-link' +
            active +
            '" href="' +
            preserveQuery(p.href) +
            '">' +
            p.label +
            "</a>"
          );
        })
        .join("") +
      "</nav>" +
      '<nav class="tool-cross" aria-label="功能分组">' +
      CROSS.map((c) => {
        const active = c.group === group ? " active" : "";
        return (
          '<a class="tool-cross-link' +
          active +
          '" href="' +
          c.href +
          '">' +
          c.label +
          "</a>"
        );
      }).join("") +
      "</nav>" +
      "</div>";

    document.body.insertBefore(bar, document.body.firstChild);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", render);
  } else {
    render();
  }
})();
