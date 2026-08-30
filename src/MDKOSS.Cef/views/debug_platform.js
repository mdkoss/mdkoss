(function () {
  const PLATFORM_TYPES = new Set(["platform", "x", "xy", "xyz", "xyzu", "xyzuv", "xyzuvw"]);
  const ALL_LETTERS = ["X", "Y", "Z", "U", "V", "W"];
  const LINEAR = new Set(["X", "Y", "Z"]);
  const KIND_AXES = {
    x: ["X"],
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
  // 三列：Col1 ±X/±U · Col2 ±Y/±V · Col3 ±Z/±W（按行交错 +/−）
  const JOG_LAYOUT = [
    ["X", 1], ["Y", 1], ["Z", 1],
    ["X", -1], ["Y", -1], ["Z", -1],
    ["U", 1], ["V", 1], ["W", 1],
    ["U", -1], ["V", -1], ["W", -1],
  ];
  const LETTER_KEYS = { x: "X", y: "Y", z: "Z", u: "U", v: "V", w: "W" };
  const DIGIT_AXES = { "1": "X", "2": "Y", "3": "Z", "4": "U", "5": "V", "6": "W" };

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
    coordMode: "joint",
    fastPoll: false,
    jogTimers: new Map(),
    teachFile: "default",
    teachPoints: [],
    selectedPointId: "P0",
    actionLog: "",
    pendingLetter: null,
  };

  const $ = (id) => document.getElementById(id);

  function showToast(msg, isErr) {
    const t = $("toast");
    if (!t) return;
    clearTimeout(showToast._t);
    clearTimeout(showToast._hide);
    t.textContent = msg;
    t.classList.remove("show", "ok", "err");
    void t.offsetWidth;
    t.classList.add("show", isErr ? "err" : "ok");
    showToast._t = setTimeout(() => {
      t.classList.remove("show");
      showToast._hide = setTimeout(() => {
        t.classList.remove("ok", "err");
        t.textContent = "";
      }, 240);
    }, 2200);
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

  function axisRows() {
    const fromDetail = state.platformDetail?.platformAxes || state.platformDetail?.PlatformAxes;
    if (Array.isArray(fromDetail) && fromDetail.length) return fromDetail;
    const dev = state.devices[state.platformId];
    const fromStatus = dev?.platformAxes || dev?.PlatformAxes;
    return Array.isArray(fromStatus) ? fromStatus : [];
  }

  function axisRow(L) {
    const letter = String(L || "").toUpperCase();
    return axisRows().find((a) => String(a.axisLetter || a.AxisLetter || "").toUpperCase() === letter);
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
    const row = axisRow(L);
    const id = row?.axisDeviceId || row?.AxisDeviceId;
    if (id) return id;
    return state.platformId + "." + L;
  }

  function varSuffix(L, s) {
    return "." + axisDeviceId(L) + "." + s;
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
    const row = axisRow(L);
    if (state.coordMode !== "pulse") {
      const pos = row?.position ?? row?.Position;
      if (pos != null && pos !== "") {
        const n = Number(pos);
        if (Number.isFinite(n)) return n;
      }
    }
    if (state.coordMode === "pulse") {
      const pulse = Number(readVarBySuffix(varSuffix(L, "pulse")));
      if (Number.isFinite(pulse)) {
        return pulse;
      }
      const raw = Number(readVarBySuffix(varSuffix(L, "rawPosition")));
      return Number.isFinite(raw) ? raw : NaN;
    }
    const n = Number(readVarBySuffix(varSuffix(L, "position")));
    return Number.isFinite(n) ? n : NaN;
  }

  function getAxisError(L) {
    const row = axisRow(L);
    const fromSnap = row?.error ?? row?.Error;
    if (fromSnap) return String(fromSnap);
    const v = readVarBySuffix(varSuffix(L, "error"));
    if (v === undefined || v === null || v === "") {
      return "";
    }
    return String(v);
  }

  function getMotionEnabled() {
    const v = readVarBySuffix("." + state.platformId + ".motionEnabled");
    return v === true || v === "true" || v === 1;
  }

  function getAxisMotionEnabled(L) {
    const row = axisRow(L);
    const snap = row?.motionEnabled ?? row?.MotionEnabled;
    if (snap === true || snap === false) {
      return snap;
    }
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
    if (state.stepPreset === "continuous") {
      return LINEAR.has(L) ? 1 : 1;
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
        const active = state.activeAxes.includes(L);
        i.closest(".form-group")?.classList.toggle("na", !active);
      }
    });
    document.querySelectorAll("#stepPresetGroup .seg-btn").forEach((el) => {
      el.classList.toggle("active", el.dataset.preset === preset);
    });
    if (preset !== "continuous" && STEP_PRESETS[preset]) {
      const pr = STEP_PRESETS[preset];
      ALL_LETTERS.forEach((L) => {
        const i = $("step_" + L);
        if (i && state.activeAxes.includes(L)) {
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
      if (p.speed && $("selSpeed")) {
        $("selSpeed").value = p.speed;
      }
      if (p.mode && $("selMode")) {
        $("selMode").value = p.mode;
      }
      if (p.coordMode) {
        state.coordMode = p.coordMode;
        document.querySelectorAll('input[name="coord"]').forEach((el) => {
          el.checked = el.value === p.coordMode;
        });
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
      JSON.stringify({
        speed: $("selSpeed").value,
        mode: $("selMode").value,
        stepPreset: state.stepPreset,
        coordMode: state.coordMode,
        steps,
      })
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
        if (!state.teachPoints.length) {
          state.teachPoints = [{ id: "P0", name: "Home", axes: {} }];
        }
        if (!state.teachPoints.some((p) => p.id === state.selectedPointId)) {
          state.selectedPointId = state.teachPoints[0].id;
        }
        return;
      }
    } catch {
      /* ignore */
    }
    state.teachPoints = [{ id: "P0", name: "Home", axes: {} }];
    state.selectedPointId = "P0";
  }

  function saveTeachPoints() {
    localStorage.setItem(
      teachStorageKey(),
      JSON.stringify({ platformId: state.platformId, kind: state.kind, points: state.teachPoints })
    );
    refreshTeachUi();
  }

  function pointDefined(p) {
    return !!(p.axes && Object.keys(p.axes).length);
  }

  function pointSummary(p) {
    if (!pointDefined(p)) {
      return "—";
    }
    return state.activeAxes
      .filter((L) => p.axes[L] != null)
      .map((L) => L + ":" + Number(p.axes[L]).toFixed(3))
      .join(" ");
  }

  function refreshTeachUi() {
    const sel = $("selPoint");
    if (sel) {
      sel.innerHTML = state.teachPoints
        .map((p) => {
          const ok = pointDefined(p);
          return `<option value="${esc(p.id)}">${esc(p.id)}: ${ok ? esc(p.name) : "(未定义)"}</option>`;
        })
        .join("");
      sel.value = state.selectedPointId;
    }
    const tbody = $("teachTableBody");
    if (tbody) {
      tbody.innerHTML = state.teachPoints
        .map((p) => {
          const ok = pointDefined(p);
          const selCls = p.id === state.selectedPointId ? " selected" : "";
          return (
            '<tr class="teach-row' +
            selCls +
            '" data-id="' +
            esc(p.id) +
            '"><td class="mono">' +
            esc(p.id) +
            "</td><td>" +
            esc(p.name || p.id) +
            '</td><td class="mono teach-sum">' +
            esc(pointSummary(p)) +
            "</td><td>" +
            (ok ? '<span class="pill ok">已定义</span>' : '<span class="pill">未定义</span>') +
            "</td></tr>"
          );
        })
        .join("");
      tbody.querySelectorAll(".teach-row").forEach((row) => {
        row.addEventListener("click", () => {
          state.selectedPointId = row.dataset.id;
          refreshTeachUi();
        });
      });
    }
    const pt = state.teachPoints.find((p) => p.id === state.selectedPointId);
    if ($("txtPointName")) {
      $("txtPointName").value = pt?.name || "";
    }
    if ($("previewPointId")) {
      $("previewPointId").textContent = pt ? pt.id : "—";
    }
    if ($("teachPreview")) {
      if (!pt) {
        $("teachPreview").textContent = "选择示教点查看坐标";
      } else if (!pointDefined(pt)) {
        $("teachPreview").textContent = pt.id + " (" + (pt.name || "") + ")\n尚未示教";
      } else {
        const lines = state.activeAxes.map((L) => {
          const v = pt.axes[L];
          return L + " = " + (v == null ? "—" : Number(v).toFixed(3)) + (LINEAR.has(L) ? " mm" : " deg");
        });
        $("teachPreview").textContent = lines.join("\n");
      }
    }
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
    if ($("actionLog")) {
      $("actionLog").textContent = state.actionLog.slice(0, 8000);
    }
    if (!res.ok || data.success === false) {
      throw new Error(data.error || "action_failed");
    }
    return data;
  }

  async function moveAxis(L, sign) {
    const mult = SPEED_MULT[$("selSpeed").value] || 1;
    const cur = getPosition(L);
    if (!Number.isFinite(cur) && state.coordMode === "pulse") {
      throw new Error("无脉冲读数");
    }
    const base = Number.isFinite(cur) ? cur : 0;
    // 点动始终按关节位置增量（脉冲模式仅显示）
    const jointPos = Number(readVarBySuffix(varSuffix(L, "position")));
    const from = Number.isFinite(jointPos) ? jointPos : base;
    const target = from + getStep(L) * mult * sign;
    await deviceAction(axisDeviceId(L), "move", { position: target });
  }

  function stopJog(L) {
    const t = state.jogTimers.get(L);
    if (t) {
      clearInterval(t);
      state.jogTimers.delete(L);
    }
    document.querySelectorAll('.jog-btn[data-letter="' + L + '"]').forEach((b) => b.classList.remove("active"));
    if (!state.jogTimers.size) {
      state.fastPoll = false;
    }
  }

  function stopAllJog() {
    [...state.jogTimers.keys()].forEach(stopJog);
    document.querySelectorAll(".jog-btn.active").forEach((b) => b.classList.remove("active"));
    state.fastPoll = false;
  }

  function startJog(L, sign, btn) {
    if (!canJog(L)) {
      return;
    }
    if (btn) {
      btn.classList.add("active");
    }
    const run = () =>
      moveAxis(L, sign).catch((e) => {
        showToast(L + " 轴: " + e.message, true);
        stopJog(L);
      });
    run();
    if (state.stepPreset !== "continuous") {
      if (btn) {
        setTimeout(() => btn.classList.remove("active"), 120);
      }
      return;
    }
    state.fastPoll = true;
    stopJog(L);
    if (btn) {
      btn.classList.add("active");
    }
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
      startJog(L, sign, btn);
    };
    const up = () => stopJog(L);
    btn.addEventListener("mousedown", down);
    btn.addEventListener("touchstart", down, { passive: false });
    btn.addEventListener("mouseup", up);
    btn.addEventListener("mouseleave", up);
    btn.addEventListener("touchend", up);
    btn.addEventListener("touchcancel", up);
  }

  function arrowFor(L, sign) {
    if (L === "X" || L === "Y") {
      return sign > 0 ? "→" : "←";
    }
    if (L === "Z") {
      return sign > 0 ? "▲" : "▼";
    }
    return sign > 0 ? "↻" : "↺";
  }

  function renderJogButtons() {
    const grid = $("jogGrid");
    if (!grid) {
      return;
    }
    grid.innerHTML = "";
    JOG_LAYOUT.forEach(([L, sign]) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "jog-btn";
      if (!state.activeAxes.includes(L)) {
        btn.classList.add("jog-ghost");
        btn.disabled = true;
        btn.setAttribute("aria-hidden", "true");
        grid.appendChild(btn);
        return;
      }
      const label = (sign > 0 ? "+" : "−") + L;
      btn.innerHTML =
        '<span class="arr">' + arrowFor(L, sign) + '</span><span class="lbl">' + label + "</span>";
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
      const wrap = $("posField_" + L);
      const inp = $("pos_" + L);
      if (!inp) {
        return;
      }
      const active = state.activeAxes.includes(L);
      if (wrap) {
        wrap.classList.toggle("na", !active);
      }
      const u = $("unit_" + L);
      if (u) {
        if (state.coordMode === "pulse") {
          u.textContent = "pls";
        } else {
          u.textContent = LINEAR.has(L) ? "mm" : "deg";
        }
      }
      if (!active) {
        inp.value = "—";
        return;
      }
      const n = getPosition(L);
      inp.value = Number.isFinite(n) ? n.toFixed(state.coordMode === "pulse" ? 0 : 3) : "—";
    });
  }

  function renderAxisTable() {
    const axes = axisRows();
    const tbody = $("axisTableBody");
    if (!tbody) {
      return;
    }
    if (!axes.length) {
      tbody.innerHTML = '<tr><td colspan="6" style="color:var(--muted)">无轴数据</td></tr>';
      renderJogButtons();
      return;
    }
    state.axisOnline = {};
    tbody.innerHTML = axes
      .map((a) => {
        const L = a.axisLetter ?? a.AxisLetter ?? "?";
        const online = !!(a.driverOnline ?? a.DriverOnline);
        state.axisOnline[L] = online;
        const st = (window.MdkTool && MdkTool.axisStatusOf) ? MdkTool.axisStatusOf(a) : (a.axisStatus || a.AxisStatus || null);
        const en = st
          ? !!(st.servoOn ?? st.ServoOn)
          : getAxisMotionEnabled(L);
        const flags = (window.MdkTool && MdkTool.renderAxisFlags)
          ? MdkTool.renderAxisFlags(st)
          : (st ? "—" : "—");
        return (
          "<tr><td><strong>" +
          esc(L) +
          '</strong></td><td class="mono">' +
          esc(axisDeviceId(L)) +
          "</td><td>" +
          esc(a.driverId ?? a.DriverId ?? "-") +
          '</td><td><span class="dot ' +
          (online ? "ok" : "err") +
          '"></span>' +
          (online ? "在线" : "离线") +
          "</td><td>" +
          (en ? '<span class="pill ok">ON</span>' : '<span class="pill">OFF</span>') +
          '</td><td><div class="flag-row">' +
          flags +
          "</div></td></tr>"
        );
      })
      .join("");
    renderJogButtons();
  }

  function updatePlatformMeta() {
    $("metaKind").textContent = state.kind;
    const motionHtml = getMotionEnabled()
      ? '<span class="pill ok">已使能</span>'
      : '<span class="pill">未使能</span>';
    $("metaMotion").innerHTML = motionHtml;
    if ($("hdrMotion")) {
      $("hdrMotion").innerHTML = state.platformId ? motionHtml : "";
    }
    const dev = state.devices[state.platformId];
    $("metaState").textContent = dev?.state ?? dev?.State ?? "—";
    $("hdrSubtitle").textContent = state.platformId
      ? ($("selPlatform").selectedOptions[0]?.textContent || "") + " · " + state.platformId
      : "请选择平台设备";
    $("warnBar").classList.toggle("hidden", !state.platformId || state.isRunning !== false);
    const lnk = $("lnkMonitor");
    const href = state.platformId
      ? "/monitor_platform.html?deviceId=" + encodeURIComponent(state.platformId)
      : "/monitor_platform.html";
    if (lnk) lnk.href = href;
    const note = $("lnkMonitorNote");
    if (note) note.href = href;
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
    applyStepPreset(state.stepPreset);
    loadTeachPoints();
    refreshTeachUi();
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
    if (state.platformId && state.devices[state.platformId]) {
      state.platformDetail = state.devices[state.platformId];
    }
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
        "<tr><td class=\"mono\">" +
          esc(id) +
          "</td><td>" +
          esc(d.name ?? d.Name ?? "-") +
          "</td><td>" +
          esc(type) +
          "</td><td>" +
          esc(d.state ?? d.State ?? "-") +
          '</td><td><a class="link-btn" href="/monitor_io.html">IO 监控</a></td></tr>'
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
    if (!state.platformId) {
      showToast("请先选择平台", true);
      return;
    }
    const axes = {};
    state.activeAxes.forEach((L) => {
      const n = Number(readVarBySuffix(varSuffix(L, "position")));
      axes[L] = Number.isFinite(n) ? n : 0;
    });
    let pt = state.teachPoints.find((p) => p.id === state.selectedPointId);
    if (!pt) {
      pt = { id: state.selectedPointId, name: state.selectedPointId, axes: {} };
      state.teachPoints.push(pt);
    }
    pt.axes = axes;
    if ($("txtPointName").value.trim()) {
      pt.name = $("txtPointName").value.trim();
    }
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

  function renamePoint() {
    const pt = state.teachPoints.find((p) => p.id === state.selectedPointId);
    if (!pt) {
      showToast("无选中点", true);
      return;
    }
    const name = $("txtPointName").value.trim();
    if (!name) {
      showToast("请输入名称", true);
      return;
    }
    pt.name = name;
    saveTeachPoints();
    showToast("已改名 " + pt.id, false);
  }

  function deletePoint() {
    if (!state.teachPoints.length) {
      return;
    }
    const id = state.selectedPointId;
    state.teachPoints = state.teachPoints.filter((p) => p.id !== id);
    if (!state.teachPoints.length) {
      state.teachPoints = [{ id: "P0", name: "Home", axes: {} }];
      state.selectedPointId = "P0";
    } else {
      state.selectedPointId = state.teachPoints[0].id;
    }
    saveTeachPoints();
    showToast("已删除 " + id, false);
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
        if (!state.teachPoints.length) {
          state.teachPoints = [{ id: "P0", name: "Home", axes: {} }];
        }
        state.selectedPointId = state.teachPoints[0].id;
        saveTeachPoints();
        showToast("示教点已导入", false);
      } catch {
        showToast("导入文件无效", true);
      }
    };
    r.readAsText(file);
  }

  function isTypingTarget(el) {
    if (!el) {
      return false;
    }
    const tag = el.tagName;
    return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
  }

  function bindKeyboard() {
    window.addEventListener("keydown", (e) => {
      if (isTypingTarget(e.target)) {
        return;
      }
      const k = e.key;
      if (k === "Escape") {
        e.preventDefault();
        stopAllJog();
        showToast("已停止点动", false);
        return;
      }
      const lower = k.length === 1 ? k.toLowerCase() : k;
      if (lower === "c") {
        e.preventDefault();
        applyStepPreset("continuous");
        return;
      }
      if (lower === "l") {
        e.preventDefault();
        applyStepPreset("long");
        return;
      }
      if (lower === "m") {
        e.preventDefault();
        applyStepPreset("medium");
        return;
      }
      if (lower === "s" && !e.ctrlKey && !e.metaKey) {
        e.preventDefault();
        applyStepPreset("short");
        return;
      }
      if (lower === "t") {
        e.preventDefault();
        teachCurrent();
        return;
      }
      if (LETTER_KEYS[lower]) {
        state.pendingLetter = LETTER_KEYS[lower];
        return;
      }
      if (DIGIT_AXES[k]) {
        state.pendingLetter = DIGIT_AXES[k];
        return;
      }
      if ((k === "+" || k === "=" || k === "-" || k === "_") && state.pendingLetter) {
        e.preventDefault();
        const L = state.pendingLetter;
        const sign = k === "-" || k === "_" ? -1 : 1;
        state.pendingLetter = null;
        if (!canJog(L)) {
          showToast(L + " 轴不可点动", true);
          return;
        }
        moveAxis(L, sign).catch((err) => showToast(L + " 轴: " + err.message, true));
      }
    });
    window.addEventListener("blur", stopAllJog);
    window.addEventListener("mouseup", () => {
      // 释放到窗口外时清理残留
      if (!state.jogTimers.size) {
        document.querySelectorAll(".jog-btn.active").forEach((b) => b.classList.remove("active"));
      }
    });
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

  $("selPlatform").addEventListener("change", () => onPlatformChange().catch((e) => showToast(e.message, true)));
  $("selSpeed").addEventListener("change", saveUiPrefs);
  $("selMode").addEventListener("change", saveUiPrefs);
  document.querySelectorAll("#stepPresetGroup .seg-btn").forEach((el) => {
    el.addEventListener("click", () => applyStepPreset(el.dataset.preset));
  });
  document.querySelectorAll('input[name="coord"]').forEach((el) => {
    el.addEventListener("change", () => {
      if (el.checked) {
        state.coordMode = el.value;
        saveUiPrefs();
        renderPositions();
      }
    });
  });
  ALL_LETTERS.forEach((L) => {
    const i = $("step_" + L);
    if (i) {
      i.addEventListener("change", saveUiPrefs);
    }
  });
  $("btnStopJog").addEventListener("click", () => {
    stopAllJog();
    showToast("已停止点动", false);
  });
  $("btnEnable").addEventListener("click", () => {
    if (!state.platformId) {
      showToast("请先选择平台", true);
      return;
    }
    if (window.MdkTool && MdkTool.confirmWrite
      && !MdkTool.confirmWrite("确认使能平台全部轴？", "平台: " + state.platformId)) {
      return;
    }
    deviceAction(state.platformId, "enable")
      .then(() => tick())
      .catch((e) => showToast(e.message, true));
  });
  $("btnDisable").addEventListener("click", () => {
    if (!state.platformId) {
      showToast("请先选择平台", true);
      return;
    }
    if (window.MdkTool && MdkTool.confirmWrite
      && !MdkTool.confirmWrite("确认去使能平台全部轴？", "平台: " + state.platformId)) {
      return;
    }
    stopAllJog();
    deviceAction(state.platformId, "disable")
      .then(() => tick())
      .catch((e) => showToast(e.message, true));
  });
  $("btnTeach").addEventListener("click", teachCurrent);
  $("btnGoto").addEventListener("click", () => {
    if (window.MdkTool && MdkTool.confirmWrite
      && !MdkTool.confirmWrite("确认定位到示教点？", "点: " + (state.selectedPointId || "—") + "\n平台: " + (state.platformId || "—"))) {
      return;
    }
    gotoPoint();
  });
  $("btnRenamePoint").addEventListener("click", renamePoint);
  $("btnDeletePoint").addEventListener("click", () => {
    if (window.MdkTool && MdkTool.confirmWrite
      && !MdkTool.confirmWrite("确认删除示教点？", "点: " + (state.selectedPointId || "—"))) {
      return;
    }
    deletePoint();
  });
  $("btnAddPoint").addEventListener("click", () => {
    const id = "P" + state.teachPoints.length;
    state.teachPoints.push({ id, name: id, axes: {} });
    state.selectedPointId = id;
    saveTeachPoints();
  });
  $("selTeachFile").addEventListener("change", () => {
    state.teachFile = $("selTeachFile").value;
    loadTeachPoints();
    refreshTeachUi();
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
  bindKeyboard();
  loadUiPrefs();
  loadPlatforms()
    .then(tick)
    .catch((e) => showToast(e.message, true));
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
