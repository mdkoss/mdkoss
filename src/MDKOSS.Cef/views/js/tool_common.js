/**
 * Shared helpers for monitor_* / debug_* / man_* tool pages.
 * Exposes window.MdkTool: esc, field, fetchJson, postJson, patchJson, toast, logLine, qs.
 */
(function (global) {
  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function field(obj, a, b) {
    if (!obj) return undefined;
    if (obj[a] !== undefined && obj[a] !== null) return obj[a];
    return obj[b];
  }

  function qs(name, fallback) {
    const v = new URLSearchParams(location.search).get(name);
    return v == null || v === "" ? fallback : v;
  }

  const AXIS_TYPES = new Set(["axis", "linear", "rotary"]);
  const PLATFORM_TYPES = new Set(["platform", "x", "xy", "xyz", "xyzu", "xyzuv", "xyzuvw"]);

  function isAxisType(t) {
    return AXIS_TYPES.has(String(t || "").toLowerCase());
  }

  function isPlatformType(t) {
    return PLATFORM_TYPES.has(String(t || "").toLowerCase());
  }

  async function fetchJson(url, options) {
    const res = await fetch(url, Object.assign({ cache: "no-store" }, options || {}));
    const data = await res.json().catch(() => ({}));
    if (!res.ok || data.success === false) {
      const err = new Error(data.error || ("http_" + res.status));
      err.status = res.status;
      err.data = data;
      throw err;
    }
    return data;
  }

  function sendJson(url, method, body) {
    return fetchJson(url, {
      method,
      headers: { "Content-Type": "application/json" },
      body: body == null ? undefined : JSON.stringify(body),
    });
  }

  function postJson(url, body) {
    return sendJson(url, "POST", body);
  }

  function patchJson(url, body) {
    return sendJson(url, "PATCH", body);
  }

  function deleteJson(url) {
    return fetchJson(url, { method: "DELETE" });
  }

  function ensureToast() {
    let el = document.getElementById("toast");
    if (el) return el;
    el = document.createElement("div");
    el.id = "toast";
    el.className = "toast";
    el.setAttribute("role", "status");
    document.body.appendChild(el);
    return el;
  }

  function hideToast(el) {
    el.classList.remove("show");
    clearTimeout(el._hide);
    el._hide = setTimeout(() => {
      el.classList.remove("ok", "err");
      el.textContent = "";
    }, 240);
  }

  function toast(msg, ok) {
    const el = ensureToast();
    clearTimeout(el._t);
    clearTimeout(el._hide);
    el.textContent = msg;
    el.classList.remove("show", "ok", "err");
    void el.offsetWidth;
    el.classList.add("show", ok === false ? "err" : "ok");
    el._t = setTimeout(() => hideToast(el), 2200);
  }

  function logLine(box, text, ok) {
    if (!box) return;
    const line = document.createElement("div");
    line.style.color = ok === false ? "var(--danger)" : ok ? "var(--ok)" : "var(--muted)";
    line.textContent = "[" + new Date().toLocaleTimeString() + "] " + text;
    box.prepend(line);
    while (box.childNodes.length > 80) box.removeChild(box.lastChild);
  }

  global.MdkTool = {
    esc,
    field,
    qs,
    isAxisType,
    isPlatformType,
    fetchJson,
    postJson,
    patchJson,
    deleteJson,
    toast,
    logLine,
  };
})(window);
