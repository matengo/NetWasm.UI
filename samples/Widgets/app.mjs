import { open } from "/browser/Widgets.mjs";
import { createDomHost } from "/nwui/dom.mjs";

// One DOM host per instance: separate node tables, listeners and event delegation roots.
async function mount(root, label) {
  const host = createDomHost({ root });
  const app = await open({ modules: { "nwui.dom": host } });
  app.start(label);
  return {
    async close() {
      app.stop();
      await app.close();
      host.dispose();
    },
  };
}

const first = await mount(document.getElementById("first"), "First");
const second = await mount(document.getElementById("second"), "Second");

document.getElementById("close-second").addEventListener("click", () => second.close(), { once: true });
document.body.dataset.ready = "true";
