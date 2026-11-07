// Injected by the NetWasm.UI dev server. Reloads the page when a new publish succeeds and keeps the
// app's state across that reload: int/bool/string signals (through dom.mjs), form fields, focus and
// scroll position. A reload you start yourself (F5) starts clean.
(() => {
  const url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/__netwasm_ui/reload";
  const storageKey = "nwui:hot";
  const fieldSelector = "input:not([type=file]):not([type=password]),textarea,select";
  const focusSelector = "input,textarea,select,button,a[href],[tabindex]";
  let overlay = null;

  let restored = null;
  try {
    restored = JSON.parse(sessionStorage.getItem(storageKey) ?? "null");
    sessionStorage.removeItem(storageKey);
  } catch {
    restored = null;
  }

  const dev = (globalThis.__nwuiDev = {
    restored,
    savers: {},
    nextId: 0,
    onRendered() {
      if (!restored || dev.onRendered.done) return;
      dev.onRendered.done = true;
      requestAnimationFrame(() => restoreDom(restored));
    },
    reload,
  });

  function reload() {
    const state = { signals: {}, fields: [], focus: null, scroll: [scrollX, scrollY] };
    for (const [id, save] of Object.entries(dev.savers)) {
      try {
        state.signals[id] = save();
      } catch (e) {
        console.warn("[netwasm.ui] could not save state", e);
      }
    }

    const fields = document.querySelectorAll(fieldSelector);
    fields.forEach((el, i) => {
      state.fields.push({ i, key: keyOf(el), value: el.value, checked: el.checked ?? false });
    });

    const active = document.activeElement;
    if (active && active !== document.body) {
      const index = Array.prototype.indexOf.call(document.querySelectorAll(focusSelector), active);
      if (index >= 0) {
        state.focus = { i: index, key: keyOf(active), start: selectionOf(active, "selectionStart"), end: selectionOf(active, "selectionEnd") };
      }
    }

    try {
      sessionStorage.setItem(storageKey, JSON.stringify(state));
    } catch (e) {
      console.warn("[netwasm.ui] could not save state", e);
    }
    location.reload();
  }

  function restoreDom(state) {
    const fields = document.querySelectorAll(fieldSelector);
    for (const f of state.fields ?? []) {
      const el = fields[f.i];
      if (!el || keyOf(el) !== f.key) continue;
      if (el.type === "checkbox" || el.type === "radio") {
        if (el.checked === f.checked) continue;
        el.checked = f.checked;
      } else {
        if (el.value === f.value) continue;
        el.value = f.value;
      }
      el.dispatchEvent(new Event("input", { bubbles: true }));
      el.dispatchEvent(new Event("change", { bubbles: true }));
    }

    const focus = state.focus;
    if (focus) {
      const el = document.querySelectorAll(focusSelector)[focus.i];
      if (el && keyOf(el) === focus.key) {
        el.focus({ preventScroll: true });
        if (focus.start !== null && typeof el.setSelectionRange === "function") {
          try {
            el.setSelectionRange(focus.start, focus.end);
          } catch {
            // Not a text field.
          }
        }
      }
    }

    if (state.scroll) scrollTo(state.scroll[0], state.scroll[1]);
  }

  // Guards against restoring into a different element after the page structure changed.
  function keyOf(el) {
    return el.tagName + "|" + (el.type ?? "") + "|" + (el.id || el.name || "");
  }

  function selectionOf(el, prop) {
    try {
      return typeof el[prop] === "number" ? el[prop] : null;
    } catch {
      return null;
    }
  }

  function showOverlay(title, text) {
    if (!overlay) {
      overlay = document.createElement("pre");
      overlay.style.cssText =
        "position:fixed;inset:0;margin:0;padding:1.5rem;overflow:auto;z-index:2147483647;" +
        "background:rgba(20,20,20,.94);color:#f8d7da;font:13px/1.4 ui-monospace,monospace;white-space:pre-wrap";
      document.documentElement.appendChild(overlay);
    }
    overlay.textContent = title + "\n\n" + text;
  }

  function hideOverlay() {
    overlay?.remove();
    overlay = null;
  }

  function connect() {
    const ws = new WebSocket(url);
    ws.onmessage = (e) => {
      const msg = JSON.parse(e.data);
      switch (msg.type) {
        case "building":
          console.info("[netwasm.ui] rebuilding…");
          break;
        case "reload":
          hideOverlay();
          reload();
          break;
        case "error":
          console.error("[netwasm.ui] build failed:\n" + msg.text);
          showOverlay("NetWasm.UI build failed", msg.text);
          break;
      }
    };
    ws.onclose = () => setTimeout(connect, 1000);
  }

  connect();
})();
