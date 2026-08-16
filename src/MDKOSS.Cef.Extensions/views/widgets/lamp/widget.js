(function () {
  const H = window.MdkHmi;
  H.register("lamp", {
    create(el, widget) {
      el.innerHTML = `<div class="hmi-w-lamp" style="width:100%;height:100%">
        <div class="bulb gray"></div>
        <div class="cap">${H.esc(H.prop(widget, "label", "指示灯"))}</div>
      </div>`;
    },
    update(el, widget, vars) {
      const v = H.varVal(vars, String(H.prop(widget, "var", "")));
      const bulb = el.querySelector(".bulb");
      if (bulb) bulb.className = "bulb " + H.lampColor(v);
    },
  });
})();
