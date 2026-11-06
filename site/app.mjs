// Relative to this module, so the site works at "/" (dev server) and under "/NetWasm.UI/" (GitHub Pages).
import { open } from "./browser/Site.mjs";

const app = await open();
app.start();
document.documentElement.dataset.ready = "true";
