(function () {
  const H = window.MdkHmi;
  H.register("button", {
    create(el, widget, ctx) {
      const style = String(H.prop(widget, "style", "default"));
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "hmi-w-button" + (style && style !== "default" ? " " + style : "");
      btn.textContent = String(H.prop(widget, "text", "按钮"));
      if (ctx.mode !== "edit") {
        btn.addEventListener("click", async () => {
          const url = String(H.prop(widget, "url", ""));
          const method = String(H.prop(widget, "method", "POST") || "POST").toUpperCase();
          if (!url) return;
          try {
            const res = await fetch(url, { method });
            if (!res.ok) throw new Error("http " + res.status);
          } catch {
            btn.textContent = "失败";
            setTimeout(() => { btn.textContent = String(H.prop(widget, "text", "按钮")); }, 1200);
          }
        });
      }
      el.appendChild(btn);
    },
  });
})();
