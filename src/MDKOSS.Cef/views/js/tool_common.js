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

  function postJson(url, body) {
    return fetchJson(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: body == null ? undefined : JSON.stringify(body),
    });
  }

  function patchJson(url, body) {
    return fetchJson(url, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body || {}),
    });
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

  function toast(msg, ok) {
    const el = ensureToast();
    el.textContent = msg;
    el.classList.remove("show", "ok", "err");
    el.classList.add("show", ok === false ? "err" : "ok");
    clearTimeout(el._t);
    el._t = setTimeout(() => el.classList.remove("show"), 2200);
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
    fetchJson,
    postJson,
    patchJson,
    toast,
    logLine,
  };
})(window);
