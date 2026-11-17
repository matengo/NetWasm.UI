import { open } from "/browser/Hosted.Client.mjs";

const app = await open();
app.start(location.origin);
document.documentElement.dataset.ready = "true";
