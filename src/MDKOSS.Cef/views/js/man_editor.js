/**
 * Simplified Config.Wpf editor: list + add/delete/duplicate + typed fields + Key/Value params.
 * body[data-man-kind]=drivers|devices|tasks|axes|platforms|recipes|alarms|visions|vars|machine
 * optional body[data-man-filter]=gpio,vio
 */
(function () {
  const KIND_META = {
    drivers: {
      api: "/api/config/drivers",
      catalog: "drivers",
      cols: ["#", "Name", "Type", "Desc", "Enable"],
      fields: { id: true, name: true, type: true, enabled: true, parameters: true },
      labels: { id: "Name (Id)", name: "Desc (描述)" },
      created: (d) => d.driver,
    },
    devices: {
      api: "/api/config/devices",
      catalog: "devices",
      cols: ["#", "Name", "Type", "Desc", "Enable"],
      fields: { id: true, name: true, type: true, driverId: true, enabled: true, parameters: true },
      labels: { id: "Name (Id)", name: "Desc (描述)" },
      created: (d) => d.device,
    },
    axes: {
      api: "/api/config/axes",
      catalog: "axes",
      cols: ["#", "Name", "Type", "Desc", "Enable"],
      fields: { id: true, name: true, type: true, driverId: true, enabled: true, parameters: true },
      labels: { id: "Name (Id)", name: "Desc (描述)" },
      created: (d) => d.device,
    },
    platforms: {
      api: "/api/config/platforms",
      catalog: "platforms",
      cols: ["#", "Name", "Type", "Desc", "Enable"],
      fields: { id: true, name: true, type: true, enabled: true, parameters: true },
      labels: { id: "Name (Id)", name: "Desc (描述)" },
      created: (d) => d.device,
    },
    tasks: {
      api: "/api/config/tasks",
      catalog: "tasks",
      cols: ["#", "Name", "Type", "Desc", ""],
      fields: { name: true, type: true, driverId: true, interval: true, parameters: true },
      labels: { name: "Name" },
      created: (d) => d.task,
    },
    recipes: {
      api: "/api/recipe",
      catalog: "",
      cols: ["#", "Name", "Type", "Desc", ""],
      fields: { id: true, name: true, description: true, parameters: true },
      labels: { id: "Name (Id)", name: "名称", description: "Description" },
      paramTitle: "Vars (Key / Value)",
      persistHint: "应用属性写入配方库",
    },
    alarms: {
      api: "/api/config/alarms",
      catalog: "",
      cols: ["#", "Name", "Code", "条件", "Enable"],
      fields: { id: true, name: true, code: true, level: true, op: true, varKey: true, value: true, description: true, enabled: true, latch: true },
      labels: { id: "Key", name: "名称", description: "消息" },
      created: (d) => d.alarm,
    },
    visions: {
      api: "/api/config/visions",
      catalog: "",
      cols: ["#", "Name", "Type", "Desc", ""],
      fields: { id: true, name: true, description: true, cameraDeviceId: true },
      labels: { id: "Name (Id)", name: "名称", description: "说明" },
      created: (d) => d.vision,
    },
    vars: {
      api: "/api/config/vars",
      catalog: "",
      cols: ["#", "Name", "", "Value", ""],
      fields: { id: true, value: true },
      labels: { id: "Key", value: "Value" },
      created: (d) => d.varItem,
    },
    machine: {
      api: "/api/config/machine",
      catalog: "",
      cols: ["#", "Name", "Type", "Desc", ""],
      fields: { parameters: true },
      labels: {},
      paramTitle: "Machine (Key / Value)",
      canCreate: false,
      canDelete: false,
      canDuplicate: false,
    },
  };

  function boot() {
    if (!window.MdkTool) return;
    const { esc, field, fetchJson, patchJson, postJson, deleteJson, toast } = MdkTool;
    const kind = (document.body.getAttribute("data-man-kind") || "").toLowerCase();
    const meta = KIND_META[kind];
    if (!meta) return;

    const filterRaw = (document.body.getAttribute("data-man-filter") || "").toLowerCase();
    const typeFilter = filterRaw
      ? new Set(filterRaw.split(",").map((s) => s.trim()).filter(Boolean))
      : null;

    const listEl = document.getElementById("manList");
    const headEl = document.getElementById("manHead");
    const formEl = document.getElementById("manForm");
    const formTitle = document.getElementById("manFormTitle");
    const metaEl = document.getElementById("manMeta");
    const filterEl = document.getElementById("manFilter");
    const quickEl = document.getElementById("manQuickAdd");
    if (!listEl || !formEl) return;

    let items = [];
    let selectedId = null;
    let catalog = { types: {}, driverIds: [], axisIds: [], cameraDeviceIds: [], defaults: {} };
    let dirty = false;

    function canCreate() { return meta.canCreate !== false; }
    function canDelete() { return meta.canDelete !== false; }
    function canDuplicate() { return meta.canDuplicate !== false; }

    function itemKey(it) {
      if (!it) return "";
      if (kind === "tasks") return it.name || it.Name || "";
      if (kind === "vars") return it.key || it.Key || it.id || "";
      if (kind === "alarms") return it.id || it.Id || it.key || it.Key || "";
      return it.id || it.Id || "";
    }

    function matchesFilter(it) {
      if (typeFilter && (kind === "devices" || kind === "axes" || kind === "platforms")) {
        if (!typeFilter.has(String(it.type || "").toLowerCase())) return false;
      }
      const q = (filterEl?.value || "").trim().toLowerCase();
      if (!q) return true;
      const blob = [
        itemKey(it), it.name, it.Name, it.type, it.Type, it.driverId, it.DriverId,
        it.description, it.code, it.varKey, it.enabled, it.key,
        stringifyValue(it.value), JSON.stringify(paramObj(it)),
      ].join(" ").toLowerCase();
      return blob.includes(q);
    }

    function stringifyValue(v) {
      if (v == null) return "";
      if (typeof v === "object") {
        try { return JSON.stringify(v); } catch { return String(v); }
      }
      return String(v);
    }

    function colType(it) {
      if (kind === "recipes") return "recipe";
      if (kind === "alarms") return it.code || it.level || "alarm";
      if (kind === "visions") return "vision";
      if (kind === "vars") return "";
      if (kind === "machine") return "machine";
      return it.type || "—";
    }

    function colDesc(it) {
      if (kind === "tasks") return it.driverId || it.parameters?.note || "";
      if (kind === "recipes") return it.name || it.description || "";
      if (kind === "alarms") return [it.varKey, it.op, it.value].filter((x) => x != null && x !== "").join(" ");
      if (kind === "visions") {
        const n = it.pipeline?.nodes?.length ?? it.Pipeline?.Nodes?.length ?? 0;
        return `${it.cameraDeviceId || "无相机"} · ${n} 节点`;
      }
      if (kind === "vars") return stringifyValue(it.value);
      if (kind === "machine") return it.parameters?.projectName || it.name || "";
      return it.name || it.id || "";
    }

    function colEnable(it) {
      if (kind === "tasks" || kind === "recipes" || kind === "visions" || kind === "vars" || kind === "machine") return "";
      return it.enabled === false ? "否" : "是";
    }

    function uniqueClientId(prefix) {
      const keys = new Set(items.map(itemKey).map((s) => String(s).toLowerCase()));
      if (!keys.has(prefix.toLowerCase())) return prefix;
      for (let i = 2; i < 1000; i++) {
        const id = `${prefix}-${i}`;
        if (!keys.has(id.toLowerCase())) return id;
      }
      return prefix + "-" + Date.now();
    }

    async function loadCatalog() {
      try {
        catalog = await fetchJson("/api/config/catalog");
      } catch {
        catalog = { types: {}, driverIds: [], axisIds: [], cameraDeviceIds: [], defaults: {} };
      }
      renderQuickAdd();
    }

    function typeOptions() {
      let types = catalog.types?.[meta.catalog] || [];
      if (typeFilter) types = types.filter((t) => typeFilter.has(String(t).toLowerCase()));
      return types;
    }

    function renderQuickAdd() {
      if (!quickEl) return;
      if (!canCreate()) {
        quickEl.innerHTML = "";
        return;
      }
      const types = typeOptions();
      if (!types.length) {
        quickEl.innerHTML = "";
        return;
      }
      quickEl.innerHTML = types
        .map((t) => `<button type="button" class="btn btn-sm" data-quick="${esc(t)}">${esc(t)}</button>`)
        .join("");
      quickEl.querySelectorAll("[data-quick]").forEach((btn) => {
        btn.onclick = () => createItem(btn.getAttribute("data-quick"));
      });
    }

    function syncToolbar() {
      const add = document.getElementById("btnAdd");
      const dup = document.getElementById("btnDup");
      const del = document.getElementById("btnDel");
      if (add) add.hidden = !canCreate();
      if (dup) dup.hidden = !canDuplicate();
      if (del) del.hidden = !canDelete();
    }

    async function loadMeta() {
      try {
        const s = await fetchJson("/api/config");
        if (metaEl) {
          const persist = meta.persistHint || "应用属性写内存，保存后需重启运行时";
          metaEl.textContent =
            `项目 ${s.projectName || "—"} · ${s.settingPath || "(未设置 SettingPath)"} · ${persist}`;
        }
      } catch (e) {
        if (metaEl) metaEl.textContent = e.message || "无法加载 /api/config";
      }
    }

    async function loadList() {
      const data = await fetchJson(meta.api);
      if (kind === "machine") {
        items = [{
          id: "machine",
          name: data.projectName || "Machine",
          type: "machine",
          parameters: data.parameters || {},
        }];
      } else if (kind === "vars") {
        items = (data.vars || []).map((it) => ({
          key: it.key || it.Key,
          value: it.value !== undefined ? it.value : it.Value,
        }));
      } else {
        const raw = field(data, kind, kind[0].toUpperCase() + kind.slice(1)) || data[kind] || data.recipes || [];
        items = raw;
      }
      renderList();
    }

    function confirmLeave() {
      if (!dirty) return true;
      return confirm("当前属性未应用，放弃修改？");
    }

    function renderList() {
      if (headEl) {
        headEl.innerHTML = "<tr>" + meta.cols.map((c) => `<th>${esc(c)}</th>`).join("") + "</tr>";
      }
      const rows = items.filter(matchesFilter);
      listEl.innerHTML = rows.length
        ? rows
            .map((it, i) => {
              const id = itemKey(it);
              const active = id === selectedId ? " selected" : "";
              return `<tr class="man-row${active}" data-id="${esc(id)}">
                <td>${i + 1}</td>
                <td class="mono">${esc(id)}</td>
                <td>${esc(colType(it))}</td>
                <td>${esc(colDesc(it))}</td>
                <td>${esc(colEnable(it))}</td>
              </tr>`;
            })
            .join("")
        : `<tr><td colspan="5" class="recipe-dialog-empty">无配置项</td></tr>`;
      listEl.querySelectorAll("tr[data-id]").forEach((tr) => {
        tr.addEventListener("click", () => {
          const id = tr.getAttribute("data-id");
          if (id === selectedId) return;
          if (!confirmLeave()) return;
          selectedId = id;
          dirty = false;
          renderList();
        });
      });
      if (selectedId && !items.some((it) => itemKey(it) === selectedId)) selectedId = null;
      if (!selectedId && items.length) selectedId = itemKey(items.filter(matchesFilter)[0] || items[0]);
      renderForm();
    }

    function current() {
      return items.find((it) => itemKey(it) === selectedId) || null;
    }

    function paramObj(it) {
      if (!it) return {};
      if (kind === "recipes") return it.vars || it.Vars || {};
      if (kind === "vars") return {};
      return it.parameters || it.Parameters || {};
    }

    function readKvRows() {
      const out = {};
      formEl.querySelectorAll(".kv-row").forEach((row) => {
        const k = (row.querySelector(".kv-key")?.value || "").trim();
        const v = row.querySelector(".kv-val")?.value ?? "";
        if (k) out[k] = v;
      });
      return out;
    }

    function kvRowsHtml(obj) {
      const entries = Object.entries(obj || {});
      if (!entries.length) return kvRowHtml("", "");
      return entries
        .sort(([a], [b]) => a.localeCompare(b, undefined, { sensitivity: "base" }))
        .map(([k, v]) => kvRowHtml(k, v == null ? "" : stringifyValue(v)))
        .join("");
    }

    function valueSuggestions() {
      if (kind === "platforms") return catalog.axisIds || [];
      return [];
    }

    function kvRowHtml(k, v) {
      const list = valueSuggestions();
      const listId = list.length ? ' list="kvValSuggest"' : "";
      return `<tr class="kv-row">
        <td><input class="kv-key" value="${esc(k)}" /></td>
        <td><input class="kv-val"${listId} value="${esc(v)}" /></td>
      </tr>`;
    }

    function optionList(values, current) {
      const set = [...new Set([...(values || []), current].filter((x) => x != null && x !== ""))];
      if (current && !set.includes(current)) set.unshift(current);
      return set
        .map((t) => `<option value="${esc(t)}"${t === current ? " selected" : ""}>${esc(t)}</option>`)
        .join("");
    }

    function dirtyBadge() {
      return dirty ? ' <span class="man-dirty">未应用</span>' : "";
    }

    function renderForm() {
      const it = current();
      const f = meta.fields;
      if (formTitle) {
        formTitle.innerHTML = (it ? `属性 — ${esc(itemKey(it))}` : "属性") + dirtyBadge();
      }
      if (!it) {
        formEl.innerHTML =
          '<div class="recipe-dialog-empty">未选择组件。可用上方类型按钮快速新建，或从列表点选后编辑。</div>';
        return;
      }

      const types = typeOptions();
      const drivers = catalog.driverIds || [];
      const cameras = catalog.cameraDeviceIds || [];
      const type = it.type || "";
      const params = paramObj(it);
      let html = "";
      if (f.id) {
        const editable = kind === "vars";
        html += `<div class="form-group"><label>${esc(meta.labels.id || "Name (Id)")}</label>
          <input id="fId" value="${esc(itemKey(it))}" ${editable ? "" : "disabled"} /></div>`;
      }
      if (f.name) {
        html += `<div class="form-group"><label>${esc(meta.labels.name || "Desc (描述)")}</label>
          <input id="fName" value="${esc(it.name || "")}" /></div>`;
      }
      if (f.type) {
        html += `<div class="form-group"><label>Type</label>
          <select id="fType">${optionList(types, type)}</select></div>`;
      }
      if (f.code) {
        html += `<div class="form-group"><label>Code</label>
          <input id="fCode" value="${esc(it.code || "")}" /></div>`;
      }
      if (f.driverId) {
        html += `<div class="form-group"><label>DriverId</label>
          <select id="fDriverId">${optionList(["", ...drivers], it.driverId || "")}</select></div>`;
      }
      if (f.cameraDeviceId) {
        html += `<div class="form-group"><label>默认相机设备</label>
          <select id="fCam">${optionList(["", ...cameras], it.cameraDeviceId || "")}</select></div>`;
      }
      if (f.interval) {
        html += `<div class="form-group"><label>IntervalMs</label>
          <input id="fInterval" type="number" min="1" value="${esc(it.intervalMs ?? 100)}" /></div>`;
      }
      if (f.level) {
        const lv = it.level || "error";
        html += `<div class="form-group"><label>级别</label>
          <select id="fLevel">${optionList(["error", "warn", "info"], lv)}</select></div>`;
      }
      if (f.op) {
        const ops = ["eq", "ne", "gt", "lt", "ge", "le", "truthy", "falsy", "empty", "nonempty"];
        html += `<div class="form-group"><label>比较</label>
          <select id="fOp">${optionList(ops, it.op || "eq")}</select></div>`;
      }
      if (f.varKey) {
        html += `<div class="form-group"><label>变量键</label>
          <input id="fVar" value="${esc(it.varKey || "")}" placeholder="task.operation.state" /></div>`;
      }
      if (f.value) {
        html += `<div class="form-group"><label>${esc(meta.labels.value || "比较值")}</label>
          <input id="fVal" value="${esc(stringifyValue(it.value))}" /></div>`;
      }
      if (f.description) {
        html += `<div class="form-group"><label>${esc(meta.labels.description || "Description")}</label>
          <input id="fDesc" value="${esc(it.description || it.message || it.msg || "")}" /></div>`;
      }
      if (f.enabled) {
        html += `<label class="form-group" style="display:flex;align-items:center;gap:8px">
          <input type="checkbox" id="fEnabled" ${it.enabled !== false ? "checked" : ""} /> Enabled
        </label>`;
      }
      if (f.latch) {
        html += `<label class="form-group" style="display:flex;align-items:center;gap:8px">
          <input type="checkbox" id="fLatch" ${it.latch ? "checked" : ""} /> 锁存
        </label>`;
      }
      if (f.parameters) {
        const suggest = valueSuggestions();
        html += `<div class="kv-head">
          <span>${esc(meta.paramTitle || "Parameters (Key / Value)")}</span>
          <div class="btn-group compact-btns">
            ${meta.catalog ? '<button type="button" class="btn btn-sm" id="btnFillTpl">补全模板</button><button type="button" class="btn btn-sm" id="btnResetTpl">重置模板</button>' : ""}
            ${kind === "recipes" ? '<button type="button" class="btn btn-sm" id="btnApplyRecipe">应用配方</button>' : ""}
            <button type="button" class="btn btn-sm" id="btnAddRow">+ 行</button>
            <button type="button" class="btn btn-sm" id="btnDelRow">删行</button>
          </div>
        </div>
        ${suggest.length ? `<datalist id="kvValSuggest">${suggest.map((id) => `<option value="${esc(id)}"></option>`).join("")}</datalist>` : ""}
        <div class="table-scroll kv-wrap">
          <table class="kv-table">
            <thead><tr><th>Key</th><th>Value</th></tr></thead>
            <tbody id="kvBody">${kvRowsHtml(params)}</tbody>
          </table>
        </div>`;
      }
      if (kind === "visions") {
        html += '<p class="man-hint">管线节点请用 Config.Wpf 视觉编辑器；此处仅配置名称与默认相机。</p>';
      }
      formEl.innerHTML = html;
      bindFormEvents();
    }

    function markDirty() {
      dirty = true;
      if (formTitle) {
        const it = current();
        formTitle.innerHTML = (it ? `属性 — ${esc(itemKey(it))}` : "属性") + dirtyBadge();
      }
    }

    function bindFormEvents() {
      formEl.querySelectorAll("input, select").forEach((el) => {
        el.addEventListener("input", markDirty);
        el.addEventListener("change", markDirty);
      });
      const add = document.getElementById("btnAddRow");
      const del = document.getElementById("btnDelRow");
      const body = document.getElementById("kvBody");
      if (add && body) {
        add.onclick = () => {
          body.insertAdjacentHTML("beforeend", kvRowHtml("", ""));
          markDirty();
        };
      }
      if (del && body) {
        del.onclick = () => {
          const last = body.querySelector(".kv-row:last-child");
          if (last) last.remove();
          markDirty();
        };
      }
      const fill = document.getElementById("btnFillTpl");
      const reset = document.getElementById("btnResetTpl");
      if (fill) fill.onclick = () => applyTemplate(true);
      if (reset) reset.onclick = () => applyTemplate(false);
      const applyR = document.getElementById("btnApplyRecipe");
      if (applyR) applyR.onclick = applyRecipe;
    }

    async function applyTemplate(fillOnly) {
      const type = document.getElementById("fType")?.value || current()?.type || "";
      const driverId = document.getElementById("fDriverId")?.value || current()?.driverId || "";
      try {
        const data = await fetchJson(
          `/api/config/catalog?module=${encodeURIComponent(meta.catalog)}&type=${encodeURIComponent(type)}&driverId=${encodeURIComponent(driverId)}`
        );
        const tpl = data.parameters || {};
        const cur = readKvRows();
        const next = fillOnly ? { ...cur } : { ...tpl };
        if (fillOnly) {
          for (const [k, v] of Object.entries(tpl)) {
            if (!(k in next) || String(next[k]).trim() === "") next[k] = v;
          }
        }
        const body = document.getElementById("kvBody");
        if (body) body.innerHTML = kvRowsHtml(next);
        markDirty();
        toast(fillOnly ? "已补全缺失参数键" : "已重置为类型默认参数", true);
      } catch (e) {
        toast(e.message || "模板失败", false);
      }
    }

    function readBody() {
      const f = meta.fields;
      const parameters = f.parameters ? readKvRows() : undefined;
      if (kind === "drivers") {
        return {
          name: document.getElementById("fName")?.value || "",
          type: document.getElementById("fType")?.value || "",
          enabled: document.getElementById("fEnabled")?.checked !== false,
          parameters,
        };
      }
      if (kind === "tasks") {
        return {
          name: document.getElementById("fName")?.value || "",
          type: document.getElementById("fType")?.value || "",
          driverId: document.getElementById("fDriverId")?.value || "",
          intervalMs: Number(document.getElementById("fInterval")?.value) || 100,
          parameters,
        };
      }
      if (kind === "recipes") {
        return {
          id: document.getElementById("fId")?.value || selectedId,
          name: document.getElementById("fName")?.value || "",
          description: document.getElementById("fDesc")?.value || "",
          vars: parameters,
        };
      }
      if (kind === "alarms") {
        return {
          code: document.getElementById("fCode")?.value || "",
          name: document.getElementById("fName")?.value || "",
          level: document.getElementById("fLevel")?.value || "error",
          op: document.getElementById("fOp")?.value || "eq",
          varKey: document.getElementById("fVar")?.value || "",
          value: document.getElementById("fVal")?.value || "",
          message: document.getElementById("fDesc")?.value || "",
          enabled: document.getElementById("fEnabled")?.checked !== false,
          latch: document.getElementById("fLatch")?.checked === true,
        };
      }
      if (kind === "visions") {
        return {
          name: document.getElementById("fName")?.value || "",
          description: document.getElementById("fDesc")?.value || "",
          cameraDeviceId: document.getElementById("fCam")?.value || "",
        };
      }
      if (kind === "vars") {
        return {
          key: document.getElementById("fId")?.value || selectedId,
          value: document.getElementById("fVal")?.value || "",
        };
      }
      if (kind === "machine") {
        return { parameters };
      }
      return {
        name: document.getElementById("fName")?.value || "",
        type: document.getElementById("fType")?.value || "",
        driverId: document.getElementById("fDriverId")?.value || "",
        enabled: document.getElementById("fEnabled")?.checked !== false,
        parameters,
      };
    }

    async function applyPatch() {
      const it = current();
      if (!it) return;
      try {
        const body = readBody();
        if (kind === "recipes") {
          await postJson("/api/recipe", body);
        } else if (kind === "machine") {
          await patchJson(meta.api, body);
        } else if (kind === "vars") {
          const data = await patchJson(`${meta.api}/${encodeURIComponent(selectedId)}`, body);
          selectedId = data.varItem?.key || body.key || selectedId;
        } else {
          await patchJson(`${meta.api}/${encodeURIComponent(selectedId)}`, body);
          if (kind === "tasks" && body.name) selectedId = body.name;
        }
        dirty = false;
        toast(kind === "recipes" ? "已写入配方库" : "已更新内存配置", true);
        await loadList();
      } catch (e) {
        toast(e.message || "应用失败", false);
      }
    }

    async function saveDisk() {
      try {
        if (dirty) await applyPatch();
        if (kind === "recipes") {
          toast("排单已写入数据库", true);
          return;
        }
        const data = await postJson("/api/config/save");
        toast(data.message || "已保存", true);
        await loadMeta();
      } catch (e) {
        toast(e.message || "保存失败", false);
      }
    }

    function createdKey(data, fallbackType) {
      const created = typeof meta.created === "function" ? meta.created(data) : null;
      return itemKey(created) || itemKey(data) || fallbackType || "";
    }

    async function createItem(preferredType) {
      if (!canCreate()) return;
      try {
        if (kind === "recipes") {
          const id = uniqueClientId("recipe-new");
          await postJson("/api/recipe", { id, name: id, description: "", vars: {} });
          selectedId = id;
        } else if (kind === "vars") {
          const data = await postJson(meta.api, { key: uniqueClientId("var.new"), value: "" });
          selectedId = createdKey(data) || data.varItem?.key;
        } else if (kind === "alarms") {
          const data = await postJson(meta.api, {
            name: "新报警",
            code: "E000",
            level: "error",
            op: "eq",
            varKey: "alarm.test",
            value: "true",
            message: "测试报警",
            latch: true,
            enabled: true,
          });
          selectedId = createdKey(data);
        } else if (kind === "visions") {
          const data = await postJson(meta.api, { name: "新视觉流程" });
          selectedId = createdKey(data);
        } else {
          const type = preferredType || typeOptions()[0] || catalog.defaults?.[meta.catalog] || "";
          const data = await postJson(meta.api, { type });
          selectedId = createdKey(data);
        }
        dirty = false;
        toast("已新建（内存）", true);
        await loadList();
      } catch (e) {
        toast(e.message || "新建失败", false);
      }
    }

    async function duplicateItem() {
      if (!canDuplicate()) return;
      const it = current();
      if (!it) { toast("请先选择组件", false); return; }
      try {
        if (kind === "recipes") {
          const id = uniqueClientId((it.id || "recipe") + "-copy");
          await postJson("/api/recipe", {
            id,
            name: (it.name || id) + " 副本",
            description: it.description || "",
            vars: paramObj(it),
          });
          selectedId = id;
        } else if (kind === "vars") {
          const data = await postJson(meta.api, {
            key: uniqueClientId(itemKey(it) + "_copy"),
            value: it.value,
          });
          selectedId = createdKey(data);
        } else if (kind === "alarms") {
          const data = await postJson(meta.api, {
            name: (it.name || itemKey(it)) + " 副本",
            code: it.code || "",
            level: it.level || "error",
            op: it.op || "eq",
            varKey: it.varKey || "",
            value: stringifyValue(it.value),
            message: it.message || it.msg || "",
            latch: !!it.latch,
            enabled: it.enabled !== false,
          });
          selectedId = createdKey(data);
        } else if (kind === "visions") {
          const data = await postJson(meta.api, {
            name: (it.name || itemKey(it)) + " 副本",
            description: it.description || "",
            cameraDeviceId: it.cameraDeviceId || "",
          });
          selectedId = createdKey(data);
        } else {
          const body = {
            type: it.type,
            name: (it.name || itemKey(it)) + " 副本",
            driverId: it.driverId || "",
            enabled: it.enabled !== false,
            intervalMs: it.intervalMs,
            parameters: paramObj(it),
          };
          const data = await postJson(meta.api, body);
          selectedId = createdKey(data);
        }
        dirty = false;
        toast("已复制（内存）", true);
        await loadList();
      } catch (e) {
        toast(e.message || "复制失败", false);
      }
    }

    async function deleteItem() {
      if (!canDelete()) return;
      const it = current();
      if (!it) { toast("请先选择组件", false); return; }
      if (!confirm(`删除 ${itemKey(it)} ？`)) return;
      try {
        if (kind === "recipes") await deleteJson(`/api/recipe/${encodeURIComponent(selectedId)}`);
        else await deleteJson(`${meta.api}/${encodeURIComponent(selectedId)}`);
        selectedId = null;
        dirty = false;
        toast("已删除（内存）", true);
        await loadList();
      } catch (e) {
        toast(e.message || "删除失败", false);
      }
    }

    async function applyRecipe() {
      if (!selectedId) return;
      try {
        await postJson(`/api/recipe/apply?id=${encodeURIComponent(selectedId)}`);
        toast("已切换配方", true);
      } catch (e) {
        toast(e.message || "应用配方失败", false);
      }
    }

    document.getElementById("btnApplyPatch")?.addEventListener("click", applyPatch);
    document.getElementById("btnSaveDisk")?.addEventListener("click", saveDisk);
    document.getElementById("btnAdd")?.addEventListener("click", () => createItem());
    document.getElementById("btnDup")?.addEventListener("click", duplicateItem);
    document.getElementById("btnDel")?.addEventListener("click", deleteItem);
    filterEl?.addEventListener("input", () => renderList());

    syncToolbar();
    loadCatalog();
    loadMeta();
    loadList().catch((e) => {
      listEl.innerHTML = `<tr><td colspan="5" class="recipe-dialog-empty">${esc(e.message)}</td></tr>`;
    });
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
  else boot();
})();
