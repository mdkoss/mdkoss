(function () {
  const PLATFORM_TYPES = new Set(["platform", "xy", "xyz", "xyzu", "xyzuv", "xyzuvw"]);
  const ALL_LETTERS = ["X", "Y", "Z", "U", "V", "W"];
  const LINEAR = new Set(["X", "Y", "Z"]);
  const KIND_AXES = {
    xy: ["X", "Y"],
    xyz: ["X", "Y", "Z"],
    xyzu: ["X", "Y", "Z", "U"],
    xyzuv: ["X", "Y", "Z", "U", "V"],
    xyzuvw: ["X", "Y", "Z", "U", "V", "W"],
  };
  const STEP_PRESETS = {
    long: { linear: 10, rotary: 5 },
    medium: { linear: 1, rotary: 1 },
    short: { linear: 0.1, rotary: 0.1 },
  };
  const SPEED_MULT = { low: 0.25, medium: 0.5, high: 1 };
  const JOG_SLOTS = [
    ["X", 1], ["X", -1], ["Y", 1], ["Y", -1], ["U", -1], ["U", 1],
    ["Y", -1], ["Y", 1], ["V", -1], ["V", 1],
    ["Z", 1], ["Z", -1], ["W", -1], ["W", 1],
  ];

  const state = {
    platforms: [],
    platformId: "",
    platformDetail: null,
    kind: "xyz",
    activeAxes: ["X", "Y", "Z"],
    vars: {},
    devices: {},
    isRunning: false,
    axisOnline: {},
    stepPreset: "continuous",
    fastPoll: false,
    jogTimers: new Map(),
    teachFile: "default",
    teachPoints: [],
    selectedPointId: "P0",
    actionLog: "",
  };

  const $ = (id) => document.getElementById(id);

  function showToast(msg, isErr) {
    const t = $("toast");
    t.textContent = msg;
    t.className = "toast show" + (isErr ? " err" : " ok");
    clearTimeout(showToast._t);
    showToast._t = setTimeout(() => t.classList.remove("show"), 3500);
  }

  function esc(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
  }

  function field(d, c, p) {
    const a = d[c];
    return a !== undefined && a !== null ? a : d[p];
  }

  function isPlatformType(t) {
    return PLATFORM_TYPES.has(String(t || "").toLowerCase());
  }

  function kindFromDevice(dev, vars) {
    const t = String(dev?.type || dev?.Type || "").toLowerCase();
    if (t !== "platform" && KIND_AXES[t]) {
      return t;
    }
    const id = state.platformId;
    for (const [k, v] of Object.entries(vars || {})) {
      if (k.includes("." + id + ".") && k.endsWith(".platformKind")) {
        return String(v).toLowerCase();
      }
    }
    const m = String(dev?.driverType || dev?.DriverType || "").match(/platform-(\w+)/i);
    return m ? m[1].toLowerCase() : "xyz";
  }

  function axesForKind(k) {
    return KIND_AXES[k] || KIND_AXES.xyz;
  }

  function axisDeviceId(L) {
    return state.platformId + "." + L;
  }

  function varSuffix(L, s) {
    return "." + state.platformId + "." + L + "." + s;
  }

  function readVarBySuffix(suf) {
    for (const [k, v] of Object.entries(state.vars)) {
      if (k.endsWith(suf) || k.includes(suf)) {
        return v;
      }
    }
    return undefined;
  }

  function getPosition(L) {
    const n = Number(readVarBySuffix(varSuffix(L, "position")));
    return Number.isFinite(n) ? n : 0;
  }

  function getMotionEnabled() {
    const v = readVarBySuffix("." + state.platformId + ".motionEnabled");
    return v === true || v === "true" || v === 1;
  }

  function getAxisMotionEnabled(L) {
    const v = readVarBySuffix(varSuffix(L, "motionEnabled"));
    if (v === undefined) {
      return getMotionEnabled();
    }
    return v === true || v === "true" || v === 1;
  }

  function getStep(L) {
    const inp = $("step_" + L);
    if (inp && !inp.disabled) {
      return Math.abs(Number(inp.value)) || 0.1;
    }
    return 1;
  }

  function applyStepPreset(preset) {
    state.stepPreset = preset;
    const dis = preset === "continuous";
    ALL_LETTERS.forEach((L) => {
      const i = $("step_" + L);
      if (i) {
        i.disabled = dis;
      }
    });
    document.querySelectorAll('input[name="stepPreset"]').forEach((el) => {
      el.checked = el.value === preset;
    });
    if (preset !== "continuous" && STEP_PRESETS[preset]) {
      const pr = STEP_PRESETS[preset];
      ALL_LETTERS.forEach((L) => {
        const i = $("step_" + L);
        if (i && !i.closest(".pos-field")?.classList.contains("na")) {
          i.value = LINEAR.has(L) ? pr.linear : pr.rotary;
        }
      });
    }
    saveUiPrefs();
  }

  function loadUiPrefs() {
    try {
      const p = JSON.parse(localStorage.getItem("mdkoss.platformJog.ui") || "null");
      if (!p) {
        return;
      }
      if (p.speed) {
        $("selSpeed").value = p.speed;
      }
      if (p.stepPreset) {
        applyStepPreset(p.stepPreset);
      }
      ALL_LETTERS.forEach((L) => {
        if (p.steps && p.steps[L] != null) {
          const i = $("step_" + L);
          if (i) {
            i.value = p.steps[L];
          }
        }
      });
    } catch {
      /* ignore */
    }
  }

  function saveUiPrefs() {
    const steps = {};
    ALL_LETTERS.forEach((L) => {
      const i = $("step_" + L);
      if (i) {
        steps[L] = i.value;
      }
    });
    localStorage.setItem(
      "mdkoss.platformJog.ui",
      JSON.stringify({ speed: $("selSpeed").value, stepPreset: state.stepPreset, steps })
    );
  }

  function teachStorageKey() {
    return "mdkoss.teach." + state.platformId + "." + state.teachFile;
  }

  function loadTeachPoints() {
    try {
      const raw = localStorage.getItem(teachStorageKey());
      if (raw) {
        state.teachPoints = JSON.parse(raw).points || [];
        return;
      }
    } catch {
      /* ignore */
    }
    state.teachPoints = [{ id: "P0", name: "Home", axes: {} }];
  }

  function saveTeachPoints() {
    localStorage.setItem(
      teachStorageKey(),
      JSON.stringify({ platformId: state.platformId, kind: state.kind, points: state.teachPoints })
    );
    refreshPointSelect();
  }

  function refreshPointSelect() {
    const sel = $("selPoint");
    if (!sel) {
      return;
    }
    sel.innerHTML = state.teachPoints
      .map((p) => {
        const ok = p.axes && Object.keys(p.axes).length;
        return `<option value="${esc(p.id)}">${esc(p.id)}: ${ok ? esc(p.name) : "(未定義)"}</option>`;
      })
      .join("");
    sel.value = state.selectedPointId;
  }

  async function apiGet(url) {
    const res = await fetch(url, { cache: "no-store" });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      throw new Error(data.error || "http " + res.status);
    }
    return data;
  }

  async function deviceAction(deviceId, action, parameters) {
    const body = parameters ? { action, parameters } : { action };
    const res = await fetch("/api/devices/" + encodeURIComponent(deviceId) + "/action", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    const data = await res.json().catch(() => ({}));
    state.actionLog =
      new Date().toLocaleTimeString() +
      " " +
      deviceId +
      " → " +
      action +
      ": " +
      JSON.stringify(data) +
      "\n" +
      state.actionLog;
    $("actionLog").textContent = state.actionLog.slice(0, 8000);
    if (!res.ok || data.success === false) {
      throw new Error(data.error || "action_failed");
    }
    return data;
  }

  async function moveAxis(L, sign) {
    const mult = SPEED_MULT[$("selSpeed").value] || 1;
    const target = getPosition(L) + getStep(L) * mult * sign;
    await deviceAction(axisDeviceId(L), "move", { position: target });
  }

  function stopJog(L) {
    const t = state.jogTimers.get(L);
    if (t) {
      clearInterval(t);
      state.jogTimers.delete(L);
    }
    if (!state.jogTimers.size) {
      state.fastPoll = false;
    }
  }

  function startJog(L, sign) {
    if (!canJog(L)) {
      return;
    }
    const run = () => moveAxis(L, sign).catch((e) => showToast(L + " 轴: " + e.message, true));
    run();
    if (state.stepPreset !== "continuous") {
      return;
    }
    state.fastPoll = true;
    stopJog(L);
    state.jogTimers.set(L, setInterval(run, 100));
  }

  function canJog(L) {
    return state.platformId && state.activeAxes.includes(L) && state.axisOnline[L] !== false;
  }

  function bindJog(btn) {
    const L = btn.dataset.letter;
    const sign = Number(btn.dataset.sign);
    const down = (e) => {
      e.preventDefault();
      startJog(L, sign);
    };
    const up = () => stopJog(L);
    btn.addEventListener("mousedown", down);
    btn.addEventListener("touchstart", down, { passive: false });
    btn.addEventListener("mouseup", up);
    btn.addEventListener("mouseleave", up);
    btn.addEventListener("touchend", up);
    btn.addEventListener("touchcancel", up);
  }

  function renderJogButtons() {
    const grid = $("jogGrid");
    grid.innerHTML = "";
    JOG_SLOTS.forEach(([L, sign]) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "jog-btn";
      if (!state.activeAxes.includes(L)) {
        btn.style.visibility = "hidden";
        btn.disabled = true;
        grid.appendChild(btn);
        return;
      }
      const label = (sign > 0 ? "+" : "−") + L;
      let arr = "↻";
      if (L === "X" || L === "Y") {
        arr = sign > 0 ? "→" : "←";
      } else if (L === "Z") {
        arr = sign > 0 ? "▲" : "▼";
      }
      btn.innerHTML = '<span class="arr">' + arr + "</span> " + label;
      btn.dataset.letter = L;
      btn.dataset.sign = String(sign);
      btn.disabled = !canJog(L);
      btn.setAttribute("aria-label", L + "轴" + (sign > 0 ? "正向" : "负向") + "点动");
      bindJog(btn);
      grid.appendChild(btn);
    });
  }

  function renderPositions() {
    ALL_LETTERS.forEach((L) => {
      const wrap = $("pos_" + L)?.closest(".pos-field");
      const inp = $("pos_" + L);
      if (!inp) {
        return;
      }
      const active = state.activeAxes.includes(L);
      if (wrap) {
        wrap.classList.toggle("na", !active);
      }
      if (!active) {
        inp.value = "—";
        inp.disabled = true;
        return;
      }
      inp.disabled = false;
      inp.value = getPosition(L).toFixed(3);
      const u = $("unit_" + L);
      if (u) {
        u.textContent = LINEAR.has(L) ? "mm" : "deg";
      }
    });
  }

  function renderAxisTable() {
    const axes = state.platformDetail?.platformAxes || state.platformDetail?.PlatformAxes || [];
    const tbody = $("axisTableBody");
    if (!axes.length) {
      tbody.innerHTML = '<tr><td colspan="6" style="color:var(--muted)">无轴数据</td></tr>';
      return;
    }
    state.axisOnline = {};
    tbody.innerHTML = axes
      .map((a) => {
        const L = a.axisLetter ?? a.AxisLetter ?? "?";
        const online = !!(a.driverOnline ?? a.DriverOnline);
        state.axisOnline[L] = online;
        const en = getAxisMotionEnabled(L);
        return (
          "<tr><td>" +
          esc(L) +
          '</td><td style="font-size:11px;font-family:monospace">' +
          esc(axisDeviceId(L)) +
          "</td><td>" +
          esc(a.driverId ?? a.DriverId ?? "-") +
          '</td><td><span class="dot ' +
          (online ? "ok" : "") +
          '"></span>' +
          (online ? "在线" : "离线") +
          "</td><td>" +
          (en ? '<span class="pill ok">ON</span>' : '<span class="pill">OFF</span>') +
          "</td><td>—</td></tr>"
        );
      })
      .join("");
    renderJogButtons();
  }

  function updatePlatformMeta() {
    $("metaKind").textContent = state.kind;
    $("metaMotion").innerHTML = getMotionEnabled()
      ? '<span class="pill ok">已使能</span>'
      : '<span class="pill">未使能</span>';
    const dev = state.devices[state.platformId];
    $("metaState").textContent = dev?.state ?? dev?.State ?? "—";
    $("hdrSubtitle").textContent = state.platformId
      ? ($("selPlatform").selectedOptions[0]?.textContent || "") + " (" + state.platformId + ")"
      : "请选择平台设备";
    $("warnBar").classList.toggle("hidden", !state.platformId || state.isRunning !== false);
  }

  async function loadPlatforms() {
    const data = await apiGet("/api/devices");
    state.platforms = (data.devices || []).filter((d) => isPlatformType(d.type));
    const sel = $("selPlatform");
    sel.innerHTML =
      '<option value="">— 选择平台 —</option>' +
      state.platforms
        .map((d) => `<option value="${esc(d.id)}">${esc(d.name || d.id)} [${esc(d.type)}]</option>`)
        .join("");
    const qid = new URLSearchParams(location.search).get("deviceId");
    if (qid && state.platforms.some((p) => p.id === qid)) {
      sel.value = qid;
    } else if (state.platforms.length === 1) {
      sel.value = state.platforms[0].id;
    }
    if (sel.value) {
      await onPlatformChange();
    }
  }

  function stopAllJog() {
    [...state.jogTimers.keys()].forEach(stopJog);
  }

  async function onPlatformChange() {
    stopAllJog();
    state.platformId = $("selPlatform").value;
    if (!state.platformId) {
      state.platformDetail = null;
      updatePlatformMeta();
      return;
    }
    const detail = await apiGet("/api/devices/" + encodeURIComponent(state.platformId));
    state.platformDetail = detail.device;
    state.devices[state.platformId] = detail.device;
    await refreshStatus();
    state.kind = kindFromDevice(detail.device, state.vars);
    state.activeAxes = axesForKind(state.kind);
    loadTeachPoints();
    refreshPointSelect();
    renderPositions();
    renderAxisTable();
    updatePlatformMeta();
    history.replaceState(null, "", "?deviceId=" + encodeURIComponent(state.platformId));
  }

  async function refreshStatus() {
    const data = await apiGet("/api/status");
    state.vars = field(data, "vars", "Vars") || {};
    state.isRunning = !!field(data, "isRunning", "IsRunning");
    state.devices = field(data, "devices", "Devices") || {};
    $("badgeRuntime").innerHTML = state.isRunning
      ? '<span class="pill ok">RUNNING</span>'
      : '<span class="pill warn">STOPPED</span>';
    $("badgeProject").textContent = field(data, "projectName", "ProjectName") || "—";
    if (state.platformId) {
      renderPositions();
      renderAxisTable();
      updatePlatformMeta();
    }
    renderIoTab();
  }

  function renderIoTab() {
    const rows = [];
    for (const [id, d] of Object.entries(state.devices)) {
      const type = String(d.type ?? d.Type ?? "").toLowerCase();
      if (type !== "gpio" && type !== "vio") {
        continue;
      }
      rows.push(
        "<tr><td>" +
          esc(id) +
          "</td><td>" +
          esc(d.name ?? d.Name ?? "-") +
          "</td><td>" +
          esc(type) +
          "</td><td>" +
          esc(d.state ?? d.State ?? "-") +
          '</td><td><a href="/monitor_io.html">IO 监控</a></td></tr>'
      );
    }
    $("ioTableBody").innerHTML = rows.length
      ? rows.join("")
      : '<tr><td colspan="5" style="color:var(--muted)">无 GPIO/VIO 设备</td></tr>';
  }

  async function tick() {
    try {
      await refreshStatus();
      $("pollHint").textContent = state.fastPoll ? "100ms（点动中）" : "1s";
    } catch (e) {
      showToast("状态刷新失败: " + e.message, true);
    }
  }

  function teachCurrent() {
    const axes = {};
    state.activeAxes.forEach((L) => {
      axes[L] = getPosition(L);
    });
    let pt = state.teachPoints.find((p) => p.id === state.selectedPointId);
    if (!pt) {
      pt = { id: state.selectedPointId, name: state.selectedPointId, axes: {} };
      state.teachPoints.push(pt);
    }
    pt.axes = axes;
    saveTeachPoints();
    showToast("已示教 " + state.selectedPointId, false);
  }

  async function gotoPoint() {
    const pt = state.teachPoints.find((p) => p.id === state.selectedPointId);
    if (!pt?.axes || !Object.keys(pt.axes).length) {
      showToast("当前点未定义", true);
      return;
    }
    try {
      await deviceAction(state.platformId, "enable");
      for (const L of state.activeAxes) {
        if (pt.axes[L] == null) {
          continue;
        }
        await deviceAction(axisDeviceId(L), "move", { position: Number(pt.axes[L]) });
      }
      showToast("已定位到 " + state.selectedPointId, false);
      await tick();
    } catch (e) {
      showToast(e.message, true);
    }
  }

  function exportTeach() {
    const blob = new Blob(
      [
        JSON.stringify(
          {
            platformId: state.platformId,
            kind: state.kind,
            file: state.teachFile,
            points: state.teachPoints,
          },
          null,
          2
        ),
      ],
      { type: "application/json" }
    );
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = (state.platformId || "platform") + ".pts.json";
    a.click();
    URL.revokeObjectURL(a.href);
  }

  function importTeach(file) {
    const r = new FileReader();
    r.onload = () => {
      try {
        state.teachPoints = JSON.parse(r.result).points || [];
        saveTeachPoints();
        showToast("示教点已导入", false);
      } catch {
        showToast("导入文件无效", true);
      }
    };
    r.readAsText(file);
  }

  function initTabs() {
    document.querySelectorAll(".tab").forEach((tab) => {
      tab.addEventListener("click", () => {
        document.querySelectorAll(".tab").forEach((t) => t.classList.remove("active"));
        document.querySelectorAll(".tab-panel").forEach((p) => p.classList.remove("active"));
        tab.classList.add("active");
        $("panel-" + tab.dataset.tab).classList.add("active");
      });
    });
  }

  $("selPlatform").addEventListener("change", () => onPlatformChange());
  $("selSpeed").addEventListener("change", saveUiPrefs);
  document.querySelectorAll('input[name="stepPreset"]').forEach((el) => {
    el.addEventListener("change", () => {
      if (el.checked) {
        applyStepPreset(el.value);
      }
    });
  });
  $("btnEnable").addEventListener("click", () =>
    deviceAction(state.platformId, "enable")
      .then(() => tick())
      .catch((e) => showToast(e.message, true))
  );
  $("btnDisable").addEventListener("click", () =>
    deviceAction(state.platformId, "disable")
      .then(() => tick())
      .catch((e) => showToast(e.message, true))
  );
  $("btnTeach").addEventListener("click", teachCurrent);
  $("btnGoto").addEventListener("click", gotoPoint);
  $("btnAddPoint").addEventListener("click", () => {
    const id = "P" + state.teachPoints.length;
    state.teachPoints.push({ id, name: id, axes: {} });
    state.selectedPointId = id;
    saveTeachPoints();
  });
  $("selPoint").addEventListener("change", () => {
    state.selectedPointId = $("selPoint").value;
  });
  $("selTeachFile").addEventListener("change", () => {
    state.teachFile = $("selTeachFile").value;
    loadTeachPoints();
    refreshPointSelect();
  });
  $("btnExport").addEventListener("click", exportTeach);
  $("importFile").addEventListener("change", (e) => {
    if (e.target.files[0]) {
      importTeach(e.target.files[0]);
    }
  });
  $("btnCustomAction").addEventListener("click", async () => {
    try {
      const action = $("customAction").value.trim();
      const params = JSON.parse($("customParams").value || "{}");
      const target = $("customDevice").value.trim() || state.platformId;
      await deviceAction(target, action, Object.keys(params).length ? params : undefined);
      showToast("动作已执行", false);
      await tick();
    } catch (e) {
      showToast(e.message, true);
    }
  });

  initTabs();
  loadUiPrefs();
  loadPlatforms().then(tick);
  setInterval(() => {
    if (!state.fastPoll) {
      tick();
    }
  }, 1000);
  setInterval(() => {
    if (state.fastPoll) {
      tick();
    }
  }, 100);
})();
