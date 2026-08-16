(function () {
  const H = window.MdkHmi;
  H.register("value", {
    create(el, widget) {
      el.innerHTML = `<div class="hmi-w-value" style="width:100%;height:100%">
        <div class="cap">${H.esc(H.prop(widget, "label", "数值"))}</div>
        <div class="val"><span class="num">—</span><span class="unit"></span></div>
      </div>`;
    },
    update(el, widget, vars) {
      const v = H.varVal(vars, String(H.prop(widget, "var", "")));
      const numEl = el.querySelector(".num");
      const unitEl = el.querySelector(".unit");
      if (numEl) numEl.textContent = v == null || v === "" ? "—" : String(v);
      if (unitEl) {
        const unit = String(H.prop(widget, "unit", ""));
        unitEl.textContent = unit;
        unitEl.style.display = unit ? "inline" : "none";
      }
    },
  });
})();
