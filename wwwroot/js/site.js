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
    var prefersDark = false;
    try {
      prefersDark = !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
    } catch (_) { }
    return prefersDark ? "dark" : "light";
  }

  function updateThemeButton(theme) {
    var btn = document.querySelector("[data-toggle-theme]");
    if (!btn) return;
    btn.textContent = theme === "dark" ? "Light mode" : "Dark mode";
    btn.setAttribute("aria-label", btn.textContent);
    btn.setAttribute("aria-pressed", theme === "dark" ? "true" : "false");
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
      if (el.id === "importProcessingDialog") return;
      closeClosestDialog(el);
      return;
    }
  });

  // Improve UX for Excel import: show immediate feedback on submit.
  window.addEventListener("DOMContentLoaded", function () {
    try {
      var form = document.getElementById("importPreviewForm");
      var btn = document.getElementById("importPreviewBtn");
      if (form && btn) {
        form.addEventListener("submit", function () {
          btn.disabled = true;
          btn.textContent = "Checking file…";
        });
      }
    } catch (_) { }
  });

  // Import commit: show processing dialog with simulated row progress (single POST has no streaming progress).
  window.addEventListener("DOMContentLoaded", function () {
    document.addEventListener(
      "submit",
      function (e) {
        var form = e.target;
        if (!form || !form.classList || !form.classList.contains("import-commit-form")) return;

        var dlg = document.getElementById("importProcessingDialog");
        if (!dlg || typeof dlg.showModal !== "function") return;

        e.preventDefault();

        var total = parseInt(form.getAttribute("data-total-rows") || "0", 10);
        if (!Number.isFinite(total) || total < 0) total = 0;

        var countsEl = document.getElementById("importProcessingCounts");
        var pctEl = document.getElementById("importProcessingPct");
        var fillEl = document.getElementById("importProcessingFill");
        var barEl = document.getElementById("importProcessingBar");
        var submitBtn = form.querySelector("button[type='submit'], input[type='submit']");

        try {
          var resultDlg = document.getElementById("importResultDialog");
          if (resultDlg && typeof resultDlg.close === "function") resultDlg.close();
        } catch (_) { }

        function setUi(processed, pct) {
          if (countsEl) countsEl.textContent = total > 0 ? processed + " / " + total : "…";
          if (pctEl) pctEl.textContent = Math.round(pct) + "%";
          if (fillEl) fillEl.style.width = Math.min(100, Math.max(0, pct)) + "%";
          if (barEl) {
            barEl.setAttribute("aria-valuenow", String(Math.round(Math.min(100, Math.max(0, pct)))));
            barEl.setAttribute("aria-valuemax", "100");
          }
        }

        setUi(0, 0);
        if (typeof dlg.showModal === "function") dlg.showModal();
        else dlg.setAttribute("open", "");

        if (submitBtn) {
          submitBtn.disabled = true;
        }

        var start = Date.now();
        var scaleSec = total > 0 ? Math.max(3, Math.min(60, total / 120)) : 8;
        var tick = setInterval(function () {
          var elapsed = (Date.now() - start) / 1000;
          var processed;
          var pct;
          if (total <= 0) {
            processed = 0;
            pct = Math.min(92, (elapsed / scaleSec) * 92);
          } else {
            var ratio = 1 - Math.exp(-elapsed / scaleSec);
            processed = Math.round(total * 0.96 * ratio);
            if (total > 1) processed = Math.min(total - 1, processed);
            else processed = 0;
            pct = (processed / total) * 100;
          }
          setUi(processed, pct);
        }, 160);

        fetch(form.action, {
          method: "POST",
          body: new FormData(form),
          credentials: "same-origin",
          redirect: "follow",
        })
          .then(function (response) {
            clearInterval(tick);
            if (total > 0) setUi(total, 100);
            else setUi(0, 100);

            if (!response.ok) {
              try {
                if (typeof dlg.close === "function") dlg.close();
              } catch (_) { }
              if (submitBtn) submitBtn.disabled = false;
              alert("Import could not complete (HTTP " + response.status + ").");
              return;
            }

            if (response.redirected) {
              window.location.assign(response.url);
              return;
            }

            return response.text().then(function (html) {
              document.open();
              document.write(html);
              document.close();
            });
          })
          .catch(function () {
            clearInterval(tick);
            try {
              if (typeof dlg.close === "function") dlg.close();
            } catch (_) { }
            if (submitBtn) submitBtn.disabled = false;
            alert("Import failed. Check your connection and try again.");
          });
      },
      true
    );
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

