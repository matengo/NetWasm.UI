// Injected by the NetWasm.UI dev server. Reloads the page when a new publish succeeds.
(() => {
  const url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/__netwasm_ui/reload";
  let overlay = null;

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
          location.reload();
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
