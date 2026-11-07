import { open } from "/browser/Interop.mjs";

const app = await open();
await app.start();
document.body.dataset.ready = "true";