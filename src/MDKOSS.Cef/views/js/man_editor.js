/**
 * Config editor aligned with MDKOSS.Config.Wpf:
 * list table + property panel (typed fields + Key/Value parameters).
 * body[data-man-kind]=drivers|devices|tasks|axes|platforms|recipes
 * optional body[data-man-filter]=gpio,vio
 */
(function () {
  const KIND_META = {
    drivers: {
      api: "/api/config/drivers",
      catalog: "drivers",
      idField: "id",
      cols: ["#", "Name", "Type", "Desc", "Enable"],
      fields: { id: true, name: true, type: true, enabled: true, parameters: true },
      labels: { id: "Name (Id)", name: "Desc (描述)" },
    },
    devices: {
      api: "/api/config/devices",
      catalog: "devices",
      idField: "id",
      cols: ["#", "Name", "Type", "Desc", "Enable"],
      fields: { id: true, name: true, type: true, driverId: true, enabled: true, parameters: true },
      labels: { id: "Name (Id)", name: "Desc (描述)" },
    },
    axes: {
      api: "/api/config/axes",
      catalog: "axes",
      idField: "id",
      cols: ["#", "Name", "Type", "Desc", "Enable"],
      fields: { id: true, name: true, type: true, driverId: true, enabled: true, parameters: true },
      labels: { id: "Name (Id)", name: "Desc (描述)" },
    },
    platforms: {
      api: "/api/config/platforms",
      catalog: "platforms",
      idField: "id",
      cols: ["#", "Name", "Type", "Desc", "Enable"],
      fields: { id: true, name: true, type: true, enabled: true, parameters: true },
      labels: { id: "Name (Id)", name: "Desc (描述)" },
    },
    tasks: {
      api: "/api/config/tasks",
      catalog: "tasks",
      idField: "name",
      cols: ["#", "Name", "Type", "Desc", ""],
      fields: { name: true, type: true, driverId: true, interval: true, parameters: true },
      labels: { name: "Name" },
    },
    recipes: {
      api: "/api/recipe",
      catalog: "",
      idField: "id",
      cols: ["#", "Name", "Type", "Desc", ""],
      fields: { id: true, name: true, description: true, parameters: true },
      labels: { id: "Name (Id)", name: "名称", description: "Description" },
      paramTitle: "Vars (Key / Value)",
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
    let catalog = { types: {}, driverIds: [], defaults: {} };
    let dirty = false;

    function itemKey(it) {
      if (kind === "tasks") return it.name || it.Name || "";
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
        it.description, it.enabled, JSON.stringify(it.parameters || it.vars || {}),
      ].join(" ").toLowerCase();
      return blob.includes(q);
    }

    function colDesc(it) {
      if (kind === "tasks") return it.driverId || it.parameters?.note || "";
      if (kind === "recipes") return it.name || it.description || "";
      return it.name || it.id || "";
    }

    function colEnable(it) {
      if (kind === "tasks" || kind === "recipes") return "";
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
      try {
        catalog = await fetchJson("/api/config/catalog");
      } catch {
        catalog = { types: {}, driverIds: [], defaults: {} };
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

    async function loadMeta() {
      try {
        const s = await fetchJson("/api/config");
        if (metaEl) {
          metaEl.textContent =
            `项目 ${s.projectName || "—"} · ${s.settingPath || "(未设置 SettingPath)"} · 应用属性写内存，保存后需重启运行时`;
        }
      } catch (e) {
        if (metaEl) metaEl.textContent = e.message || "无法加载 /api/config";
      }
    }

    async function loadList() {
      const data = await fetchJson(meta.api);
      const raw = field(data, kind, kind[0].toUpperCase() + kind.slice(1)) || data[kind] || data.recipes || [];
      items = raw;
      renderList();
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
                <td>${esc(it.type || (kind === "recipes" ? "recipe" : "—"))}</td>
                <td>${esc(colDesc(it))}</td>
                <td>${esc(colEnable(it))}</td>
              </tr>`;
            })
            .join("")
        : `<tr><td colspan="5" class="recipe-dialog-empty">无配置项</td></tr>`;
      listEl.querySelectorAll("tr[data-id]").forEach((tr) => {
        tr.addEventListener("click", () => {
          selectedId = tr.getAttribute("data-id");
          dirty = false;
          renderList();
          renderForm();
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
      if (kind === "recipes") return it.vars || it.Vars || {};
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
      if (!entries.length) {
        return kvRowHtml("", "");
      }
      return entries
        .sort(([a], [b]) => a.localeCompare(b, undefined, { sensitivity: "base" }))
        .map(([k, v]) => kvRowHtml(k, v == null ? "" : String(v)))
        .join("");
    }

    function kvRowHtml(k, v) {
      return `<tr class="kv-row">
        <td><input class="kv-key" value="${esc(k)}" /></td>
        <td><input class="kv-val" value="${esc(v)}" /></td>
      </tr>`;
    }

    function optionList(values, current) {
      const set = [...new Set([...(values || []), current].filter((x) => x != null && x !== ""))];
      if (current && !set.includes(current)) set.unshift(current);
      return set
        .map((t) => `<option value="${esc(t)}"${t === current ? " selected" : ""}>${esc(t)}</option>`)
        .join("");
    }

    function renderForm() {
      const it = current();
      const f = meta.fields;
      if (formTitle) formTitle.textContent = it ? `属性 — ${itemKey(it)}` : "属性";
      if (!it) {
        formEl.innerHTML =
          '<div class="recipe-dialog-empty">未选择组件。可用上方类型按钮快速新建，或从列表点选后编辑。</div>';
        return;
      }

      const types = typeOptions();
      const drivers = catalog.driverIds || [];
      const type = it.type || "";
      const params = paramObj(it);
      let html = "";
      if (f.id) {
        html += `<div class="form-group"><label>${esc(meta.labels.id || "Name (Id)")}</label>
          <input id="fId" value="${esc(it.id || "")}" disabled /></div>`;
      }
      if (f.name) {
        html += `<div class="form-group"><label>${esc(meta.labels.name || "Desc (描述)")}</label>
          <input id="fName" value="${esc(it.name || "")}" /></div>`;
      }
      if (f.type) {
        html += `<div class="form-group"><label>Type</label>
          <select id="fType">${optionList(types, type)}</select></div>`;
      }
      if (f.driverId) {
        html += `<div class="form-group"><label>DriverId</label>
          <select id="fDriverId">${optionList(["", ...drivers], it.driverId || "")}</select></div>`;
      }
      if (f.interval) {
        html += `<div class="form-group"><label>IntervalMs</label>
          <input id="fInterval" type="number" min="1" value="${esc(it.intervalMs ?? 100)}" /></div>`;
      }
      if (f.description) {
        html += `<div class="form-group"><label>${esc(meta.labels.description || "Description")}</label>
          <input id="fDesc" value="${esc(it.description || "")}" /></div>`;
      }
      if (f.enabled) {
        html += `<label class="form-group" style="display:flex;align-items:center;gap:8px">
          <input type="checkbox" id="fEnabled" ${it.enabled !== false ? "checked" : ""} /> Enabled
        </label>`;
      }
      if (f.parameters) {
        html += `<div class="kv-head">
          <span>${esc(meta.paramTitle || "Parameters (Key / Value)")}</span>
          <div class="btn-group compact-btns">
            ${kind !== "recipes" ? '<button type="button" class="btn btn-sm" id="btnFillTpl">补全模板</button><button type="button" class="btn btn-sm" id="btnResetTpl">重置模板</button>' : ""}
            ${kind === "recipes" ? '<button type="button" class="btn btn-sm" id="btnApplyRecipe">应用排单</button>' : ""}
            <button type="button" class="btn btn-sm" id="btnAddRow">+ 行</button>
            <button type="button" class="btn btn-sm" id="btnDelRow">删行</button>
          </div>
        </div>
        <div class="table-scroll kv-wrap">
          <table class="kv-table">
            <thead><tr><th>Key</th><th>Value</th></tr></thead>
            <tbody id="kvBody">${kvRowsHtml(params)}</tbody>
          </table>
        </div>`;
      }
      formEl.innerHTML = html;
      bindFormEvents();
    }

    function bindFormEvents() {
      formEl.querySelectorAll("input, select").forEach((el) => {
        el.addEventListener("input", () => { dirty = true; });
        el.addEventListener("change", () => { dirty = true; });
      });
      const add = document.getElementById("btnAddRow");
      const del = document.getElementById("btnDelRow");
      const body = document.getElementById("kvBody");
      if (add && body) {
        add.onclick = () => {
          body.insertAdjacentHTML("beforeend", kvRowHtml("", ""));
          dirty = true;
        };
      }
      if (del && body) {
        del.onclick = () => {
          const last = body.querySelector(".kv-row:last-child");
          if (last) last.remove();
          dirty = true;
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
            if (!next[k] || String(next[k]).trim() === "") next[k] = v;
          }
        }
        const body = document.getElementById("kvBody");
        if (body) body.innerHTML = kvRowsHtml(next);
        dirty = true;
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
        } else {
          await patchJson(`${meta.api}/${encodeURIComponent(selectedId)}`, body);
        }
        dirty = false;
        toast("已更新内存配置", true);
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

    async function createItem(preferredType) {
      try {
        let data;
        if (kind === "recipes") {
          const id = uniqueClientId("recipe-new");
          await postJson("/api/recipe", { id, name: id, description: "", vars: {} });
          selectedId = id;
        } else {
          const data = await postJson(meta.api, { type: preferredType || catalog.defaults?.[meta.catalog] || "" });
          selectedId = itemKey(data.driver || data.device || data.task || {});
        }
        toast("已新建（内存）", true);
        await loadList();
      } catch (e) {
        toast(e.message || "新建失败", false);
      }
    }

    async function duplicateItem() {
      const it = current();
      if (!it) { toast("请先选择组件", false); return; }
      try {
        const body = {
          type: it.type,
          name: (it.name || itemKey(it)) + " 副本",
          driverId: it.driverId || "",
          enabled: it.enabled !== false,
          intervalMs: it.intervalMs,
          parameters: paramObj(it),
        };
        if (kind === "recipes") {
          const id = uniqueClientId((it.id || "recipe") + "-copy");
          await postJson("/api/recipe", {
            id,
            name: (it.name || id) + " 副本",
            description: it.description || "",
            vars: paramObj(it),
          });
          selectedId = id;
        } else {
          const data = await postJson(meta.api, body);
          selectedId = itemKey(data.driver || data.device || data.task || {});
        }
        toast("已复制（内存）", true);
        await loadList();
      } catch (e) {
        toast(e.message || "复制失败", false);
      }
    }

    async function deleteItem() {
      const it = current();
      if (!it) { toast("请先选择组件", false); return; }
      if (!confirm(`删除 ${itemKey(it)} ？`)) return;
      try {
        if (kind === "recipes") await deleteJson(`/api/recipe/${encodeURIComponent(selectedId)}`);
        else await deleteJson(`${meta.api}/${encodeURIComponent(selectedId)}`);
        selectedId = null;
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
        toast("已切换排单", true);
      } catch (e) {
        toast(e.message || "应用排单失败", false);
      }
    }

    document.getElementById("btnApplyPatch")?.addEventListener("click", applyPatch);
    document.getElementById("btnSaveDisk")?.addEventListener("click", saveDisk);
    document.getElementById("btnAdd")?.addEventListener("click", () => createItem());
    document.getElementById("btnDup")?.addEventListener("click", duplicateItem);
    document.getElementById("btnDel")?.addEventListener("click", deleteItem);
    filterEl?.addEventListener("input", renderList);

    loadCatalog();
    loadMeta();
    loadList().catch((e) => {
      listEl.innerHTML = `<tr><td colspan="5" class="recipe-dialog-empty">${esc(e.message)}</td></tr>`;
    });
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
  else boot();
})();
