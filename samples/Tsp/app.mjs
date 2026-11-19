import { open } from "./browser/Tsp.mjs";

const app = await open();
app.start();
document.body.dataset.ready = "true";
