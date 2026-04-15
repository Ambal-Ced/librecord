// Preserve scroll position across Razor Page postbacks.
// This keeps Admin actions (Up/Down/Toggle) from jumping to the top.
(function () {
  var KEY = "librecord.scrollY";
  var THEME_KEY = "librecord.theme";

  function applyTheme(theme) {
    var t = theme === "dark" ? "dark" : "light";
    document.documentElement.setAttribute("data-theme", t);
    try { localStorage.setItem(THEME_KEY, t); } catch (_) { }
    updateThemeButton(t);
  }

  function getTheme() {
    try {
      var t = localStorage.getItem(THEME_KEY);
      if (t === "dark" || t === "light") return t;
    } catch (_) { }
    return "light";
  }

  function updateThemeButton(theme) {
    var btn = document.querySelector("[data-toggle-theme]");
    if (!btn) return;
    btn.textContent = theme === "dark" ? "Light mode" : "Dark mode";
    btn.setAttribute("aria-label", btn.textContent);
  }

  // Apply persisted theme as early as possible.
  applyTheme(getTheme());

  function saveScroll() {
    try {
      sessionStorage.setItem(KEY, String(window.scrollY || 0));
    } catch (_) { }
  }

  // Save as early as possible when navigation starts.
  window.addEventListener("beforeunload", saveScroll);

  // Also save on form submits/clicks to avoid the brief jump to top
  // that can happen before beforeunload fires.
  document.addEventListener("submit", function () { saveScroll(); }, true);
  document.addEventListener("click", function (e) {
    var t = e.target;
    if (!t) return;
    // If clicking inside a submit button (common in our Admin action forms).
    if (t.closest && t.closest("button[type='submit'], input[type='submit']")) saveScroll();
  }, true);

  function openDialogById(id) {
    var d = document.getElementById(id);
    if (!d || typeof d.showModal !== "function") return;
    try { d.showModal(); } catch (_) { }
  }

  function closeClosestDialog(el) {
    if (!el || !el.closest) return;
    var d = el.closest("dialog");
    if (!d) return;
    try { d.close(); } catch (_) { }
  }

  document.addEventListener("click", function (e) {
    var el = e.target;
    if (!el || !el.closest) return;
    var themeBtn = el.closest("[data-toggle-theme]");
    if (themeBtn) {
      var next = (document.documentElement.getAttribute("data-theme") === "dark") ? "light" : "dark";
      applyTheme(next);
      return;
    }
    var related = el.closest("[data-open-related]");
    if (related) {
      var details = related.querySelector(".related-card__details");
      var body = document.getElementById("relatedDetailsDialogBody");
      if (details && body) body.innerHTML = details.innerHTML;
      openDialogById("relatedDetailsDialog");
      return;
    }
    var openBtn = el.closest("[data-open-dialog]");
    if (openBtn) {
      var id = openBtn.getAttribute("data-open-dialog");
      if (id) openDialogById(id);
      return;
    }

    if (el.closest("[data-close-dialog]")) {
      closeClosestDialog(el);
      return;
    }

    // click outside dialog panel closes it
    if (el.nodeName === "DIALOG") {
      closeClosestDialog(el);
      return;
    }
  });

  window.addEventListener("DOMContentLoaded", function () {
    try {
      var v = sessionStorage.getItem(KEY);
      if (!v) return;
      sessionStorage.removeItem(KEY);
      var y = parseInt(v, 10);
      if (!Number.isFinite(y) || y <= 0) return;

      // Restore after layout settles.
      requestAnimationFrame(function () {
        requestAnimationFrame(function () {
          window.scrollTo(0, y);
        });
      });
    } catch (_) { }
  });
})();

