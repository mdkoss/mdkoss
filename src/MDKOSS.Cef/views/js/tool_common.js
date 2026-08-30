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

  const AXIS_FLAGS = [
    { key: "alarm", code: "ALM", title: "驱动器报警", fault: true },
    { key: "followError", code: "FE", title: "跟随误差越限", fault: true },
    { key: "positiveLimitLevel", code: "PLL", title: "正限位电平", fault: false },
    { key: "positiveLimit", code: "EL+", title: "正限位", fault: true },
    { key: "negativeLimit", code: "EL-", title: "负限位", fault: true },
    { key: "smoothStop", code: "SSTP", title: "平滑停止", fault: false },
    { key: "abruptStop", code: "ESTP", title: "急停", fault: true },
    { key: "servoOn", code: "SVON", title: "伺服使能", fault: false },
    { key: "moving", code: "MOVE", title: "规划运动", fault: false },
    { key: "inPosition", code: "INP", title: "电机到位", fault: false },
    { key: "home", code: "ORG", title: "原点", fault: false },
  ];

  function pick(obj, camel, pascal) {
    if (!obj) return undefined;
    if (obj[camel] !== undefined && obj[camel] !== null) return obj[camel];
    if (obj[pascal] !== undefined && obj[pascal] !== null) return obj[pascal];
    return undefined;
  }

  function axisStatusOf(obj) {
    return pick(obj, "axisStatus", "AxisStatus") || null;
  }

  function flagOn(status, key) {
    if (!status) return false;
    const pascal = key.charAt(0).toUpperCase() + key.slice(1);
    return !!(status[key] ?? status[pascal]);
  }

  function renderAxisFlags(status) {
    if (!status) {
      return AXIS_FLAGS.map((f) => `<span class="flag" title="${esc(f.title)}">${esc(f.code)}</span>`).join("");
    }
    return AXIS_FLAGS.map((f) => {
      const on = flagOn(status, f.key);
      const cls = on ? (f.fault ? "flag fault" : "flag on") : "flag";
      return `<span class="${cls}" title="${esc(f.title)}">${esc(f.code)}</span>`;
    }).join("");
  }

  function fmtAxisNum(n, digits) {
    const v = Number(n);
    if (!Number.isFinite(v)) return "—";
    const d = digits == null ? (Math.abs(v) >= 100 ? 2 : 3) : digits;
    return v.toFixed(d);
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

  /** Industrial write confirm. Returns true if user accepts.
   *  Policy: one-shot dangerous writes (enable/disable/move/force/delete/execute) → confirm;
   *  continuous/high-freq (jog hold, step, serial send, query/ping) → no confirm.
   */
  function confirmWrite(action, detail) {
    const msg = detail ? action + "\n\n" + detail : action;
    return window.confirm(msg);
  }

  function toneOnline(online, fault) {
    if (fault) return "fault";
    if (online === true) return "ok";
    if (online === false) return "warn";
    return "";
  }

  function pillOnline(online) {
    if (online === true) return '<span class="pill ok">在线</span>';
    if (online === false) return '<span class="pill warn">离线</span>';
    return '<span class="pill">—</span>';
  }

  /**
   * Sticky alarm strip under tool chrome (monitor / debug).
   * Polls GET /api/alarms every 2s.
   */
  function startAlarmBar(options) {
    const opts = options || {};
    if (document.getElementById("toolAlarmBar")) return;
    const bar = document.createElement("div");
    bar.id = "toolAlarmBar";
    bar.className = "tool-alarm-bar idle";
    bar.innerHTML =
      '<a class="alarm-bar-link" href="/monitor_alarm.html">' +
      '<span class="alarm-bar-dot"></span>' +
      '<span class="alarm-bar-text" id="toolAlarmText">报警：检测中…</span>' +
      '<span class="alarm-bar-meta" id="toolAlarmMeta"></span>' +
      "</a>";
    const chrome = document.getElementById("toolChrome");
    if (chrome && chrome.parentNode) {
      chrome.parentNode.insertBefore(bar, chrome.nextSibling);
    } else {
      document.body.insertBefore(bar, document.body.firstChild);
    }

    async function tick() {
      try {
        const data = await fetchJson("/api/alarms");
        const active = Number(data.activeCount ?? 0);
        const errN = Number(data.errorCount ?? 0);
        const warnN = Number(data.warnCount ?? 0);
        const unacked = Number(data.unackedCount ?? 0);
        const text = document.getElementById("toolAlarmText");
        const meta = document.getElementById("toolAlarmMeta");
        bar.classList.remove("idle", "ok", "warn", "fault");
        if (active <= 0) {
          bar.classList.add("ok");
          if (text) text.textContent = "无活动报警";
          if (meta) meta.textContent = "";
        } else {
          bar.classList.add(errN > 0 ? "fault" : "warn");
          if (text) {
            text.textContent =
              "活动报警 " + active + (errN ? " · 错误 " + errN : "") + (warnN ? " · 警告 " + warnN : "");
          }
          if (meta) meta.textContent = unacked ? "未确认 " + unacked : "全部已确认";
        }
      } catch {
        bar.classList.remove("ok", "warn", "fault");
        bar.classList.add("idle");
        const text = document.getElementById("toolAlarmText");
        if (text) text.textContent = "报警接口不可用";
      }
    }

    tick();
    const ms = opts.intervalMs || 2000;
    setInterval(tick, ms);
  }

  global.MdkTool = {
    esc,
    field,
    qs,
    isAxisType,
    isPlatformType,
    axisStatusOf,
    renderAxisFlags,
    flagOn,
    fmtAxisNum,
    fetchJson,
    postJson,
    patchJson,
    deleteJson,
    toast,
    logLine,
    confirmWrite,
    toneOnline,
    pillOnline,
    startAlarmBar,
  };
})(window);
