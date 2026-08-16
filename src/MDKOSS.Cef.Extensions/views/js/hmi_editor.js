/**
 * Simple main-HMI layout editor (palette + canvas + props).
 */
(function () {
  const H = window.MdkHmi;
  const canvas = document.getElementById("hmiCanvas");
  const palette = document.getElementById("hmiPalette");
  const propsForm = document.getElementById("hmiProps");
  const titleEl = document.getElementById("hmiTitle");
  const metaEl = document.getElementById("hmiMeta");

  let layout = { version: 1, title: "主界面监控", canvasWidth: 1180, canvasHeight: 520, widgets: [] };
  let catalog = [];
  let selectedId = null;
  let drag = null;

  function toast(msg, kind) {
    if (window.MdkTool && MdkTool.toast) MdkTool.toast(msg, kind);
    else if (metaEl) metaEl.textContent = msg;
  }

  function findWidget(id) {
    return layout.widgets.find((w) => w.id === id) || null;
  }

  function redraw() {
    H.render(canvas, layout, "edit");
    H.bindLayout(canvas, layout);
    canvas.querySelectorAll(".hmi-widget").forEach((el) => {
      el.classList.toggle("selected", el.dataset.id === selectedId);
      el.addEventListener("pointerdown", onPointerDown);
    });
    renderProps();
  }

  function onPointerDown(e) {
    const el = e.currentTarget;
    selectedId = el.dataset.id;
    const w = findWidget(selectedId);
    if (!w) return;
    const rect = canvas.getBoundingClientRect();
    drag = {
      id: selectedId,
      dx: e.clientX - rect.left + canvas.scrollLeft - w.x,
      dy: e.clientY - rect.top + canvas.scrollTop - w.y,
    };
    el.setPointerCapture(e.pointerId);
    canvas.querySelectorAll(".hmi-widget").forEach((n) => n.classList.toggle("selected", n.dataset.id === selectedId));
    renderProps();
    e.preventDefault();
  }

  canvas.addEventListener("pointermove", (e) => {
    if (!drag) return;
    const w = findWidget(drag.id);
    if (!w) return;
    const rect = canvas.getBoundingClientRect();
    w.x = Math.max(0, Math.round(e.clientX - rect.left + canvas.scrollLeft - drag.dx));
    w.y = Math.max(0, Math.round(e.clientY - rect.top + canvas.scrollTop - drag.dy));
    const el = canvas.querySelector(`.hmi-widget[data-id="${CSS.escape(drag.id)}"]`);
    if (el) {
      el.style.left = w.x + "px";
      el.style.top = w.y + "px";
    }
  });

  canvas.addEventListener("pointerup", () => { drag = null; });
  canvas.addEventListener("click", (e) => {
    if (e.target === canvas) {
      selectedId = null;
      canvas.querySelectorAll(".hmi-widget.selected").forEach((n) => n.classList.remove("selected"));
      renderProps();
    }
  });

  document.addEventListener("keydown", (e) => {
    if ((e.key === "Delete" || e.key === "Backspace") && selectedId && !isFormField(e.target)) {
      layout.widgets = layout.widgets.filter((w) => w.id !== selectedId);
      selectedId = null;
      redraw();
    }
  });

  function isFormField(el) {
    const tag = (el && el.tagName || "").toLowerCase();
    return tag === "input" || tag === "select" || tag === "textarea";
  }

  function addWidget(type) {
    const desc = catalog.find((c) => c.type === type);
    const props = {};
    (desc && desc.props || []).forEach((p) => {
      if (p.default !== undefined && p.default !== null) props[p.key] = p.kind === "number" ? Number(p.default) : p.default;
    });
    const widget = {
      id: "w-" + Math.random().toString(16).slice(2, 10),
      type,
      x: 24 + (layout.widgets.length % 6) * 16,
      y: 24 + (layout.widgets.length % 6) * 16,
      w: (desc && desc.defaultW) || 160,
      h: (desc && desc.defaultH) || 48,
      props,
    };
    layout.widgets.push(widget);
    selectedId = widget.id;
    redraw();
  }

  function renderPalette() {
    palette.innerHTML = "";
    catalog.forEach((item) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "btn";
      btn.textContent = item.displayName + " · " + item.type;
      btn.addEventListener("click", () => addWidget(item.type));
      palette.appendChild(btn);
    });
  }

  function setProp(widget, key, value, kind) {
    widget.props = widget.props || {};
    if (key === "x" || key === "y" || key === "w" || key === "h") {
      widget[key] = Number(value) || 0;
      return;
    }
    widget.props[key] = kind === "number" ? Number(value) : value;
  }

  function renderProps() {
    const w = findWidget(selectedId);
    if (!w) {
      propsForm.innerHTML = '<div class="sub">点选画布中的控件，或从左侧添加。</div>';
      return;
    }
    const desc = catalog.find((c) => c.type === w.type);
    const rows = [
      field("x", "X", "number", w.x),
      field("y", "Y", "number", w.y),
      field("w", "宽", "number", w.w),
      field("h", "高", "number", w.h),
    ];
    (desc && desc.props || []).forEach((p) => {
      const cur = w.props && (w.props[p.key] ?? p.default ?? "");
      rows.push(field(p.key, p.label, p.kind === "select" ? "select" : p.kind, cur, p.options));
    });
    propsForm.innerHTML = `<div class="label">${H.esc(w.type)} · ${H.esc(w.id)}</div>` + rows.join("");
    propsForm.querySelectorAll("[data-key]").forEach((input) => {
      input.addEventListener("change", () => {
        setProp(w, input.getAttribute("data-key"), input.value, input.getAttribute("data-kind"));
        redraw();
      });
    });
  }

  function field(key, label, kind, value, options) {
    if (kind === "select") {
      const opts = (options || []).map((o) => {
        const sel = String(o) === String(value) ? " selected" : "";
        return `<option value="${H.esc(o)}"${sel}>${H.esc(o)}</option>`;
      }).join("");
      return `<div class="hmi-prop-row"><label>${H.esc(label)}</label><select data-key="${H.esc(key)}" data-kind="select">${opts}</select></div>`;
    }
    const t = kind === "number" ? "number" : "text";
    return `<div class="hmi-prop-row"><label>${H.esc(label)}</label><input data-key="${H.esc(key)}" data-kind="${H.esc(kind)}" type="${t}" value="${H.esc(value)}" /></div>`;
  }

  async function load() {
    const [layoutRes, catRes] = await Promise.all([
      fetch("/api/hmi/layout", { cache: "no-store" }),
      fetch("/api/hmi/widgets", { cache: "no-store" }),
    ]);
    if (!layoutRes.ok) throw new Error("layout " + layoutRes.status);
    const data = await layoutRes.json();
    layout = data.layout || layout;
    if (titleEl) titleEl.value = layout.title || "";
    if (metaEl) metaEl.textContent = data.path || "";
    if (catRes.ok) {
      const cat = await catRes.json();
      catalog = cat.widgets || [];
      await H.loadExtensions(cat);
    }
    renderPalette();
    redraw();
  }

  async function save() {
    layout.title = titleEl ? titleEl.value.trim() || "主界面监控" : layout.title;
    const res = await fetch("/api/hmi/layout", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(layout),
    });
    if (!res.ok) throw new Error("save " + res.status);
    toast("已保存组态", "ok");
  }

  async function reset() {
    const res = await fetch("/api/hmi/reset", { method: "POST" });
    if (!res.ok) throw new Error("reset " + res.status);
    const data = await res.json();
    layout = data.layout || layout;
    if (titleEl) titleEl.value = layout.title || "";
    selectedId = null;
    redraw();
    toast("已恢复默认组态", "ok");
  }

  document.getElementById("btnSave").addEventListener("click", () => save().catch((e) => toast(String(e), "err")));
  document.getElementById("btnReset").addEventListener("click", () => reset().catch((e) => toast(String(e), "err")));
  document.getElementById("btnPreview").addEventListener("click", () => { location.href = "/index_hmi.html"; });
  if (titleEl) {
    titleEl.addEventListener("change", () => { layout.title = titleEl.value.trim() || "主界面监控"; });
  }

  load().catch((e) => {
    if (metaEl) metaEl.textContent = "无法加载 /api/hmi（扩展未注册？） " + e;
  });
})();
