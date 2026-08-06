/**
 * Shared light config editor for man_* pages.
 * body[data-man-kind]=drivers|devices|tasks
 * optional body[data-man-filter]=axis|platform|gpio (comma types for devices)
 */
(function () {
  function boot() {
    if (!window.MdkTool) return;
    const { esc, field, fetchJson, patchJson, postJson, toast } = MdkTool;
    const kind = (document.body.getAttribute("data-man-kind") || "").toLowerCase();
    const filterRaw = (document.body.getAttribute("data-man-filter") || "").toLowerCase();
    const typeFilter = filterRaw
      ? new Set(filterRaw.split(",").map((s) => s.trim()).filter(Boolean))
      : null;

    if (!["drivers", "devices", "tasks"].includes(kind)) return;

    const listEl = document.getElementById("manList");
    const formEl = document.getElementById("manForm");
    const metaEl = document.getElementById("manMeta");
    const btnSave = document.getElementById("btnSaveDisk");
    const btnApply = document.getElementById("btnApplyPatch");
    if (!listEl || !formEl) return;

    let items = [];
    let selectedId = null;

    function itemKey(it) {
      if (kind === "tasks") return it.name || it.Name || "";
      return it.id || it.Id || "";
    }

    function itemLabel(it) {
      if (kind === "tasks") return `${it.name || ""} · ${it.type || ""}`;
      if (kind === "devices") return `${it.name || it.id} (${it.type})`;
      return `${it.id} · ${it.type || ""}`;
    }

    function matchesFilter(it) {
      if (!typeFilter || kind !== "devices") return true;
      return typeFilter.has(String(it.type || "").toLowerCase());
    }

    async function loadMeta() {
      try {
        const s = await fetchJson("/api/config");
        if (metaEl) {
          metaEl.textContent = `项目 ${s.projectName || "—"} · ${s.settingPath || "(未设置 SettingPath)"}`;
        }
      } catch (e) {
        if (metaEl) metaEl.textContent = e.message || "无法加载 /api/config";
      }
    }

    async function loadList() {
      const data = await fetchJson(`/api/config/${kind}`);
      items = (field(data, kind, kind[0].toUpperCase() + kind.slice(1)) || data[kind] || []).filter(matchesFilter);
      const cur = selectedId;
      listEl.innerHTML = items.length
        ? items
            .map((it) => {
              const id = itemKey(it);
              const active = id === cur ? " active" : "";
              return `<button type="button" class="recipe-option${active}" data-id="${esc(id)}">
                <div class="recipe-option-name">${esc(itemLabel(it))}</div>
                <div class="recipe-option-meta">${esc(id)}</div>
              </button>`;
            })
            .join("")
        : '<div class="recipe-dialog-empty">无配置项</div>';

      listEl.querySelectorAll("button[data-id]").forEach((btn) => {
        btn.addEventListener("click", () => {
          selectedId = btn.getAttribute("data-id");
          renderForm();
          loadList().catch(() => {});
        });
      });

      if (selectedId && !items.some((it) => itemKey(it) === selectedId)) selectedId = null;
      if (!selectedId && items.length) selectedId = itemKey(items[0]);
      renderForm();
    }

    function current() {
      return items.find((it) => itemKey(it) === selectedId) || null;
    }

    function renderForm() {
      const it = current();
      if (!it) {
        formEl.innerHTML = '<div class="recipe-dialog-empty">请选择左侧项</div>';
        return;
      }

      const params = it.parameters || it.Parameters || {};
      let paramsText = "";
      try {
        paramsText = JSON.stringify(params, null, 2);
      } catch {
        paramsText = "{}";
      }

      if (kind === "drivers") {
        formEl.innerHTML = `
          <div class="form-group"><label>ID</label><input type="text" id="fId" value="${esc(it.id)}" disabled /></div>
          <div class="form-group"><label>Type</label><input type="text" id="fType" value="${esc(it.type || "")}" /></div>
          <label class="form-group" style="display:flex;align-items:center;gap:8px">
            <input type="checkbox" id="fEnabled" ${it.enabled !== false ? "checked" : ""} /> Enabled
          </label>
          <div class="form-group"><label>Parameters (JSON object)</label>
            <textarea id="fParams" rows="10" style="width:100%;font-family:monospace;font-size:12px">${esc(paramsText)}</textarea>
          </div>`;
      } else if (kind === "devices") {
        formEl.innerHTML = `
          <div class="form-group"><label>ID</label><input type="text" id="fId" value="${esc(it.id)}" disabled /></div>
          <div class="form-group"><label>Name</label><input type="text" id="fName" value="${esc(it.name || "")}" /></div>
          <div class="form-group"><label>Type</label><input type="text" id="fType" value="${esc(it.type || "")}" /></div>
          <div class="form-group"><label>DriverId</label><input type="text" id="fDriverId" value="${esc(it.driverId || "")}" /></div>
          <label class="form-group" style="display:flex;align-items:center;gap:8px">
            <input type="checkbox" id="fEnabled" ${it.enabled !== false ? "checked" : ""} /> Enabled
          </label>
          <div class="form-group"><label>Parameters (JSON object)</label>
            <textarea id="fParams" rows="10" style="width:100%;font-family:monospace;font-size:12px">${esc(paramsText)}</textarea>
          </div>`;
      } else {
        formEl.innerHTML = `
          <div class="form-group"><label>Name</label><input type="text" id="fName" value="${esc(it.name || "")}" /></div>
          <div class="form-group"><label>Type</label><input type="text" id="fType" value="${esc(it.type || "")}" /></div>
          <div class="form-group"><label>DriverId</label><input type="text" id="fDriverId" value="${esc(it.driverId || "")}" /></div>
          <div class="form-group"><label>IntervalMs</label><input type="number" id="fInterval" value="${esc(it.intervalMs ?? 100)}" /></div>
          <div class="form-group"><label>Parameters (JSON object)</label>
            <textarea id="fParams" rows="10" style="width:100%;font-family:monospace;font-size:12px">${esc(paramsText)}</textarea>
          </div>`;
      }
    }

    function parseParams() {
      const raw = document.getElementById("fParams")?.value || "{}";
      const obj = JSON.parse(raw);
      if (!obj || typeof obj !== "object" || Array.isArray(obj)) throw new Error("parameters_must_be_object");
      const out = {};
      for (const [k, v] of Object.entries(obj)) out[k] = v == null ? "" : String(v);
      return out;
    }

    async function applyPatch() {
      const it = current();
      if (!it) return;
      let body;
      try {
        const parameters = parseParams();
        if (kind === "drivers") {
          body = {
            enabled: document.getElementById("fEnabled").checked,
            type: document.getElementById("fType").value,
            parameters,
          };
        } else if (kind === "devices") {
          body = {
            name: document.getElementById("fName").value,
            type: document.getElementById("fType").value,
            driverId: document.getElementById("fDriverId").value,
            enabled: document.getElementById("fEnabled").checked,
            parameters,
          };
        } else {
          body = {
            name: document.getElementById("fName").value,
            type: document.getElementById("fType").value,
            driverId: document.getElementById("fDriverId").value,
            intervalMs: Number(document.getElementById("fInterval").value) || 100,
            parameters,
          };
        }
      } catch (e) {
        toast("Parameters JSON 无效: " + e.message, false);
        return;
      }

      try {
        const id = encodeURIComponent(selectedId);
        await patchJson(`/api/config/${kind}/${id}`, body);
        toast("已更新内存配置", true);
        await loadList();
      } catch (e) {
        toast(e.message || "PATCH 失败", false);
      }
    }

    async function saveDisk() {
      try {
        const data = await postJson("/api/config/save");
        toast(data.message || "已保存", true);
        await loadMeta();
      } catch (e) {
        toast(e.message || "保存失败", false);
      }
    }

    if (btnApply) btnApply.addEventListener("click", applyPatch);
    if (btnSave) btnSave.addEventListener("click", saveDisk);

    loadMeta();
    loadList().catch((e) => {
      listEl.innerHTML = `<div class="recipe-dialog-empty">${esc(e.message)}</div>`;
    });
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
  else boot();
})();
