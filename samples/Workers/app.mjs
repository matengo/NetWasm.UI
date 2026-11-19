import { open } from "/browser/Workers.mjs";

const app = await open();
app.start();
document.body.dataset.ready = "true";
