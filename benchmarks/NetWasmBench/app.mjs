import { open } from "./browser/NetWasmBench.mjs";

const app = await open();
app.start();
window.bench = { reactive: name => app.reactive(name) };
window.__ready = performance.now();
