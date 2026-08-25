/**
 * Shared bootstrap for popup_*.html when opened as index iframe (?embedded=1).
 * Hides standalone title and forwards Escape to the parent shell.
 */
(function () {
  if (new URLSearchParams(location.search).get("embedded") !== "1") return;
  document.addEventListener("DOMContentLoaded", function () {
    document.body.classList.add("embedded");
  });
  if (document.body) document.body.classList.add("embedded");

  document.addEventListener("keydown", function (e) {
    if (e.key !== "Escape") return;
    e.preventDefault();
    try {
      parent.postMessage({ type: "mdkoss-popup-close", refresh: false }, "*");
    } catch {
      /* ignore cross-frame errors */
    }
  });
})();
