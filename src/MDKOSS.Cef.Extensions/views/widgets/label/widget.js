(function () {
  const H = window.MdkHmi;
  H.register("label", {
    create(el, widget) {
      el.classList.add("hmi-w-label");
      el.style.justifyContent = ({ left: "flex-start", center: "center", right: "flex-end" })[H.prop(widget, "align", "left")] || "flex-start";
      el.style.fontSize = H.num(widget, "fontSize", 16) + "px";
      el.textContent = String(H.prop(widget, "text", "文本"));
    },
  });
})();
