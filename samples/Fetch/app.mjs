import { open } from "/browser/Fetch.mjs";

const app = await open();
app.start(location.origin);
document.body.dataset.ready = "true";
