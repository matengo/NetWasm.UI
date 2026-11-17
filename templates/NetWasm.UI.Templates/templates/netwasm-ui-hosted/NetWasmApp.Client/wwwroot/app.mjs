import { open } from "/browser/NetWasmApp.Client.mjs";

const app = await open();
app.start(location.origin);
document.documentElement.dataset.ready = "true";
