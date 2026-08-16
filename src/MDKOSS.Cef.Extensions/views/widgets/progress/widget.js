(function () {
  const H = window.MdkHmi;
  H.register("progress", {
    create(el, widget) {
      el.innerHTML = `<div class="hmi-w-progress" style="width:100%;height:100%">
        <div class="row"><span>${H.esc(H.prop(widget, "label", "进度"))}</span><span class="pct">—</span></div>
        <div class="bar"><div class="fill"></div></div>
      </div>`;
    },
    update(el, widget, vars) {
      const v = H.varVal(vars, String(H.prop(widget, "var", "")));
      const min = H.num(widget, "min", 0);
      const max = H.num(widget, "max", 100);
      const n = Number(v);
      const pct = !Number.isFinite(n) || max === min
        ? 0
        : Math.max(0, Math.min(100, ((n - min) / (max - min)) * 100));
      const fill = el.querySelector(".fill");
      const label = el.querySelector(".pct");
      if (fill) fill.style.width = pct + "%";
      if (label) label.textContent = Number.isFinite(n) ? (Math.round(pct) + "%") : "—";
    },
  });
})();
