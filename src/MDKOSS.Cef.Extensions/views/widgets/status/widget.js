(function () {
  const H = window.MdkHmi;
  H.register("status", {
    create(el, widget) {
      el.innerHTML = `<div class="hmi-w-status" style="width:100%;height:100%">
        <span class="dot"></span><span class="txt">${H.esc(H.prop(widget, "label", "状态"))}</span>
      </div>`;
    },
    update(el, widget, vars) {
      const v = H.varVal(vars, String(H.prop(widget, "var", "")));
      const dot = el.querySelector(".dot");
      if (dot) dot.className = "dot " + H.statusMode(widget, v);
    },
  });
})();
