/**
 * Main-HMI widget host. Exposes window.MdkHmi.
 * Widget types register via MdkHmi.register(type, { create, update }).
 * Built-in and extra packs load from GET /api/hmi/widgets (script / css URLs).
 */
(function (global) {
  const renderers = Object.create(null);

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function prop(widget, key, fallback) {
    const p = widget && widget.props ? widget.props : {};
    const v = p[key] !== undefined ? p[key] : p[key && key[0].toUpperCase() + key.slice(1)];
    if (v === undefined || v === null || v === "") return fallback;
    return v;
  }

  function num(widget, key, fallback) {
    const n = Number(prop(widget, key, fallback));
    return Number.isFinite(n) ? n : fallback;
  }

  function varVal(vars, key) {
    if (!vars || !key) return undefined;
    if (vars[key] !== undefined) return vars[key];
    const lower = String(key).toLowerCase();
    for (const k of Object.keys(vars)) {
      if (k.toLowerCase() === lower) return vars[k];
    }
    return undefined;
  }

  function truthy(v) {
    if (v === true || v === 1 || v === "1" || v === "true" || v === "True") return true;
    if (typeof v === "string") {
      const s = v.trim().toLowerCase();
      return s === "on" || s === "yes" || s === "running" || s === "ok";
    }
    return false;
  }

  function lampColor(v) {
    const s = String(v ?? "").trim().toLowerCase();
    if (s === "green" || s === "ok" || s === "1" || s === "true") return "green";
    if (s === "yellow" || s === "warn" || s === "2") return "yellow";
    if (s === "red" || s === "fault" || s === "error" || s === "3") return "red";
    if (v === true || v === 1) return "green";
    if (v === false || v === 0 || v == null || v === "") return "gray";
    return "red";
  }

  function statusMode(widget, v) {
    const when = String(prop(widget, "okWhen", "truthy")).toLowerCase();
    if (when === "zero") return Number(v) === 0 ? "ok" : "bad";
    if (when === "falsy") return truthy(v) ? "bad" : "ok";
    if (when === "equals") {
      return String(v) === String(prop(widget, "okValue", "")) ? "ok" : "warn";
    }
    const s = String(v ?? "").toLowerCase();
    if (s === "fault" || s === "error") return "bad";
    if (s === "running" || s === "ok") return "ok";
    return truthy(v) ? "ok" : "warn";
  }

  function helpers() {
    return { esc, prop, num, varVal, truthy, lampColor, statusMode };
  }

  function applyBox(el, widget) {
    el.style.left = (widget.x || 0) + "px";
    el.style.top = (widget.y || 0) + "px";
    el.style.width = (widget.w || 80) + "px";
    el.style.height = (widget.h || 32) + "px";
    el.dataset.id = widget.id || "";
    el.dataset.type = widget.type || "";
  }

  function register(type, handlers) {
    if (!type || !handlers) return;
    renderers[String(type).toLowerCase()] = handlers;
  }

  function loadCss(url) {
    if (!url || document.querySelector(`link[data-hmi-widget="${url}"]`)) return;
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = url;
    link.dataset.hmiWidget = url;
    document.head.appendChild(link);
  }

  function loadScript(url) {
    return new Promise((resolve, reject) => {
      if (!url) {
        resolve();
        return;
      }
      if (document.querySelector(`script[data-hmi-widget="${url}"]`)) {
        resolve();
        return;
      }
      const script = document.createElement("script");
      script.src = url;
      script.async = false;
      script.dataset.hmiWidget = url;
      script.onload = () => resolve();
      script.onerror = () => reject(new Error("widget script " + url));
      document.head.appendChild(script);
    });
  }

  async function loadExtensions(catalog) {
    const widgets = catalog && catalog.widgets
      ? catalog.widgets
      : (Array.isArray(catalog) ? catalog : []);
    widgets.forEach((w) => {
      if (w && w.css) loadCss(w.css);
    });
    for (const w of widgets) {
      if (w && w.script) await loadScript(w.script);
    }
    return widgets;
  }

  let readyPromise = null;

  function ready() {
    if (readyPromise) return readyPromise;
    readyPromise = (async () => {
      const res = await fetch("/api/hmi/widgets", { cache: "no-store" });
      if (!res.ok) throw new Error("widgets " + res.status);
      const data = await res.json();
      await loadExtensions(data);
      return data;
    })();
    return readyPromise;
  }

  function createWidgetEl(widget, mode) {
    const type = String(widget.type || "").toLowerCase();
    const wrap = document.createElement("div");
    wrap.className = "hmi-widget" + (mode === "edit" ? " edit" : "");
    applyBox(wrap, widget);
    const renderer = renderers[type];
    const ctx = Object.assign({ mode }, helpers());
    if (renderer && typeof renderer.create === "function") {
      renderer.create(wrap, widget, ctx);
    } else {
      wrap.classList.add("hmi-w-label");
      wrap.textContent = type || "unknown";
    }
    return wrap;
  }

  function render(container, layout, mode) {
    container.innerHTML = "";
    container.classList.add("hmi-canvas");
    container.classList.toggle("edit", mode === "edit");
    const w = (layout && layout.canvasWidth) || 1180;
    const h = (layout && layout.canvasHeight) || 520;
    container.style.minWidth = w + "px";
    container.style.minHeight = h + "px";
    const widgets = (layout && layout.widgets) || [];
    widgets.forEach((widget) => container.appendChild(createWidgetEl(widget, mode)));
  }

  function update(container, vars) {
    const nodes = container.querySelectorAll(".hmi-widget");
    const ctx = Object.assign({ mode: "run" }, helpers());
    nodes.forEach((el) => {
      const type = String(el.dataset.type || "").toLowerCase();
      const id = el.dataset.id;
      const widget = (container._layoutWidgets || []).find((w) => w.id === id) || null;
      if (!widget) return;
      const renderer = renderers[type];
      if (renderer && typeof renderer.update === "function") {
        renderer.update(el, widget, vars, ctx);
      }
    });
  }

  function bindLayout(container, layout) {
    container._layoutWidgets = (layout && layout.widgets) || [];
  }

  global.MdkHmi = {
    esc,
    prop,
    num,
    varVal,
    truthy,
    lampColor,
    statusMode,
    register,
    loadExtensions,
    ready,
    createWidgetEl,
    render,
    update,
    bindLayout,
  };
})(window);
