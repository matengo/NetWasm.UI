// NetWasm.UI publish-time prerendering (see NetWasm.UI.Prerender.targets and docs/deployment.md).
//
// Runs the published app in Node against an in-memory DOM: the bundled "nwui.dom" module picks up
// globalThis.__nwuiPrerenderHost, records the render batches, and the mounted tree is serialized into the
// mount element of the page. The browser shows that HTML at once; when the Wasm app starts it replaces it
// (dom.mjs clears a mount target marked data-nwui-prerender).
//
//   node prerender.mjs --dir <publish dir> --module browser/App.mjs --html index.html
//                      [--export start] [--routes "/;/about=about.html"] [--base /app/] [--shell 404.html]
//
// Each route renders in its own Node process (a Wasm instance and its static state serve one route).

import { spawnSync } from "node:child_process";
import { readFileSync, writeFileSync, mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import v8 from "node:v8";


function parseArgs(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (!a.startsWith("--")) continue;
    const next = argv[i + 1];
    out[a.slice(2)] = next === undefined || next.startsWith("--") ? true : (i++, next);
  }
  return out;
}

// ---- parent: render every route and write the pages ----

const START = "<!--nwui:prerender-->";
const END = "<!--/nwui:prerender-->";

function main(a) {
  const dir = resolve(a.dir ?? ".");
  const htmlPath = resolve(dir, a.html ?? "index.html");
  let shell;
  try {
    shell = strip(readFileSync(htmlPath, "utf8"));
  } catch {
    console.log(`NetWasm.UI prerender: ${htmlPath} not found, skipped.`);
    return 0;
  }

  const base = normalizeBase(a.base && a.base !== true ? a.base : documentBase(shell));
  const routes = String(a.routes && a.routes !== true ? a.routes : "/")
    .split(";").map(r => r.trim()).filter(Boolean)
    .map(r => {
      const eq = r.indexOf("=");
      const path = eq < 0 ? r : r.slice(0, eq).trim();
      const output = eq < 0 ? "" : r.slice(eq + 1).trim();
      return { path: path.startsWith("/") ? path : "/" + path, output };
    })
    // A route listed twice renders once, with the last output given for it.
    .filter((r, i, all) => all.findLastIndex(o => o.path === r.path) === i);

  if (a.shell && a.shell !== true) writeFileSync(resolve(dir, a.shell), shell);

  const work = mkdtempSync(join(tmpdir(), "nwui-prerender-"));
  try {
    for (const route of routes) {
      const result = join(work, "result.json");
      const child = spawnSync(process.execPath, [
        fileURLToPath(import.meta.url), "--render",
        "--dir", dir, "--module", a.module ?? "", "--export", a.export && a.export !== true ? a.export : "start",
        "--path", base + route.path, "--base", base, "--result", result,
      ], { encoding: "utf8", timeout: 60_000 });

      let r = null;
      try { r = JSON.parse(readFileSync(result, "utf8")); } catch { /* reported below */ }
      rmSync(result, { force: true });
      if (child.status !== 0 || !r || r.error) {
        const detail = r?.error ?? (child.stderr || child.stdout || child.error?.message || `exit code ${child.status}`).trim();
        warn(`route ${route.path} failed: ${detail.split("\n").slice(0, 6).join(" | ")}`);
        continue;
      }

      const html = inject(shell, r);
      if (html === null) {
        warn(`mount target '${r.selector}' not found in ${htmlPath} (only an empty element selected by #id is supported).`);
        continue;
      }

      const out = resolve(dir, route.output || defaultOutput(route.path, a.html ?? "index.html"));
      mkdirSync(dirname(out), { recursive: true });
      writeFileSync(out, html);
      console.log(`NetWasm.UI prerender: ${route.path} -> ${out} (${Buffer.byteLength(r.html)} bytes)`);
    }
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
  // Failed routes are reported as warnings (see the targets file); their pages stay as they were.
  return 0;
}

function warn(message) {
  console.log(`NetWasm.UI prerender warning: ${message}`);
}

// Removes a previous prerender, so publishing twice gives the same page and --shell gets the bare page.
function strip(html) {
  return html
    .replace(/<!--nwui:prerender-->[\s\S]*?<!--\/nwui:prerender-->/g, "")
    .replace(/\sdata-nwui-prerender(="[^"]*")?/g, "");
}

function inject(shell, r) {
  const m = /^#([\w-]+)$/.exec(r.selector);
  if (!m) return null;
  const open = new RegExp(`<([a-zA-Z][\\w-]*)((?:\\s[^>]*?)?\\sid=(["']?)${m[1]}\\3(?:[\\s/][^>]*)?)>(\\s*)</\\1>`);
  const found = open.exec(shell);
  if (!found) return null;
  const [all, tag, attrs] = found;
  let html = shell.slice(0, found.index)
    + `<${tag}${attrs} data-nwui-prerender>${START}${r.html}${END}</${tag}>`
    + shell.slice(found.index + all.length);
  if (r.title) html = html.replace(/<title>[\s\S]*?<\/title>/i, `<title>${escapeText(r.title)}</title>`);
  return html;
}

// "/" -> index.html, "/about" -> about.html, "/docs/intro" -> docs/intro.html: hosts such as GitHub Pages,
// Netlify and Cloudflare Pages serve /about from about.html, without redirecting to /about/.
function defaultOutput(path, html) {
  const p = path.split(/[?#]/)[0].replace(/^\/+|\/+$/g, "");
  return p === "" ? html : p + ".html";
}

function documentBase(html) {
  const m = /<base\s[^>]*href=["']?([^"'\s>]*)/i.exec(html);
  if (!m) return "";
  try { return new URL(m[1], "http://x/").pathname; } catch { return ""; }
}

function normalizeBase(b) {
  const t = String(b ?? "").replace(/^\/+|\/+$/g, "");
  return t === "" ? "" : "/" + t;
}

// ---- child: render one route ----

async function renderRoute(a) {
  const write = r => writeFileSync(a.result, JSON.stringify(r));
  try {
    write(await render(a));
    process.exit(0);
  } catch (e) {
    write({ error: e?.stack ?? String(e) });
    process.exit(1);
  }
}

async function render(a) {
  // NetWasm needs Wasm exception handling with exnref; Node 22–24 have it behind a flag.
  const probe = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 4, 1, 96, 0, 0, 3, 2, 1, 0, 10, 8, 1, 6, 0, 31, 64, 0, 11, 11]);
  if (!WebAssembly.validate(probe)) v8.setFlagsFromString("--experimental-wasm-exnref");

  const dir = resolve(a.dir);
  const baseUrl = pathToFileURL(dir + "/").href;
  installPlatformShims();
  const doc = { title: "", baseURI: baseUrl, querySelector: () => null, querySelectorAll: () => [], documentElement: { dataset: {} } };
  globalThis.document ??= doc;

  const dom = createPrerenderHost({ path: a.path, base: a.base ?? "" });
  globalThis.__nwuiPrerenderHost = dom.host;

  const mod = await import(new URL(a.module, baseUrl).href);
  const app = await mod.open();
  const start = app[a.export];
  if (typeof start !== "function") throw new Error(`the module has no export '${a.export}'`);
  try {
    await start.call(app);
  } catch (e) {
    throw dom.error ?? e;
  }

  // Let effects that settle right away (timers, resolved promises, local fetches) render, then snapshot.
  const began = Date.now();
  while (Date.now() - began < 3000 && Date.now() - dom.lastBatch < 100) await new Promise(r => setTimeout(r, 20));
  if (dom.error) throw dom.error;
  if (dom.batches === 0) throw new Error("the app rendered nothing");
  if (!dom.mount) throw new Error("the app did not mount");

  return { selector: dom.mount.selector, html: dom.mount.nodes.map(serialize).join(""), title: globalThis.document.title || "" };
}

function installPlatformShims() {
  const realFetch = globalThis.fetch;
  globalThis.fetch = async (input, init) => {
    const url = new URL(typeof input === "string" || input instanceof URL ? String(input) : input.url);
    if (url.protocol !== "file:") return realFetch(input, init);
    try {
      const body = await readFile(url);
      const type = url.pathname.endsWith(".wasm") ? "application/wasm"
        : url.pathname.endsWith(".json") ? "application/json"
        : url.pathname.endsWith(".mjs") || url.pathname.endsWith(".js") ? "text/javascript" : "application/octet-stream";
      return new Response(body, { status: 200, headers: { "content-type": type } });
    } catch {
      return new Response("Not Found", { status: 404, statusText: "Not Found" });
    }
  };

  // The runtime imports verified modules through blob: URLs, which Node's ESM loader rejects.
  const parts = new WeakMap();
  const NodeBlob = globalThis.Blob;
  globalThis.Blob = class extends NodeBlob {
    constructor(p = [], o) { super(p, o); parts.set(this, p); }
  };
  URL.createObjectURL = blob => {
    const chunks = (parts.get(blob) ?? []).map(x => typeof x === "string" ? Buffer.from(x)
      : ArrayBuffer.isView(x) ? Buffer.from(x.buffer, x.byteOffset, x.byteLength) : Buffer.from(x));
    return `data:${blob.type || "text/javascript"};base64,` + Buffer.concat(chunks).toString("base64");
  };
  URL.revokeObjectURL = () => {};
}

// ---- the in-memory DOM host ----

const VOID = new Set(["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"]);
const RAW_TEXT = new Set(["script", "style"]);
const BOOLEAN_PROPS = new Set(["checked", "selected", "disabled", "hidden", "readonly", "required", "multiple", "open", "autofocus"]);

function element(tag) { return { type: 1, tag: tag.toLowerCase(), attrs: new Map(), children: [], parent: null }; }
function text(data, raw = false) { return { type: 3, data, raw, parent: null }; }
function comment(data) { return { type: 8, data, parent: null }; }

function detach(n) {
  const p = n.parent;
  if (!p) return;
  const i = p.children.indexOf(n);
  if (i >= 0) p.children.splice(i, 1);
  n.parent = null;
}

function append(parent, child) { detach(child); parent.children.push(child); child.parent = parent; }

function insertBefore(ref, child) {
  const p = ref.parent;
  if (!p) return;
  detach(child);
  p.children.splice(p.children.indexOf(ref), 0, child);
  child.parent = p;
}

function clone(n) {
  if (n.type === 3) return text(n.data, n.raw);
  if (n.type === 8) return comment(n.data);
  const c = element(n.tag);
  for (const [k, v] of n.attrs) c.attrs.set(k, v);
  for (const child of n.children) append(c, clone(child));
  return c;
}

function createPrerenderHost({ path, base }) {
  const nodes = new Map();
  const templates = new Map();
  const state = { batches: 0, lastBatch: Date.now(), mount: null, host: null, error: null };

  const setAttr = (n, name, value, raw = false) => n.attrs.set(name, { value, raw });

  function apply(bytes) {
    const r = new Reader(bytes);
    while (!r.done) {
      const op = r.u8();
      switch (op) {
        case 1: { const id = r.i32(); nodes.set(id, element(r.str())); break; }
        case 2: { const id = r.i32(); nodes.set(id, text(r.str())); break; }
        case 3: nodes.set(r.i32(), comment("")); break;
        case 4: { const n = nodes.get(r.i32()); n.data = r.str(); n.raw = false; break; }
        case 5: { const n = nodes.get(r.i32()); const name = r.str(); setAttr(n, name, r.str()); break; }
        case 6: { const n = nodes.get(r.i32()); n.attrs.delete(r.str()); break; }
        case 7: {
          const n = nodes.get(r.i32());
          const name = r.str();
          const value = r.str();
          if (name === "value" && n.tag === "textarea") { n.children.length = 0; append(n, text(value)); }
          else if (name === "value") setAttr(n, "value", value);
          else if (name === "className") setAttr(n, "class", value);
          else if (name === "textContent") { n.children.length = 0; append(n, text(value)); }
          break;
        }
        case 8: {
          const n = nodes.get(r.i32());
          const name = r.str().toLowerCase();
          const on = r.u8() !== 0;
          if (BOOLEAN_PROPS.has(name)) { if (on) setAttr(n, name, ""); else n.attrs.delete(name); }
          break;
        }
        case 9: {
          const parent = nodes.get(r.i32());
          const child = nodes.get(r.i32());
          if (parent.type === 8) insertBefore(parent, child); else append(parent, child);
          break;
        }
        case 10: { const ref = nodes.get(r.i32()); insertBefore(ref, nodes.get(r.i32())); break; }
        case 11: {
          const n = nodes.get(r.i32());
          if (n) {
            detach(n);
            if (state.mount) state.mount.nodes = state.mount.nodes.filter(m => m !== n);
          }
          break;
        }
        case 12: nodes.delete(r.i32()); break;
        case 13: r.i32(); r.str(); r.i32(); r.u8(); break;
        case 14: r.i32(); r.i32(); break;
        case 15: {
          const n = nodes.get(r.i32());
          const selector = r.str();
          state.mount ??= { selector, nodes: [] };
          if (state.mount.selector === selector) state.mount.nodes.push(n);
          break;
        }
        case 16: r.i32(); break;
        case 17: { const id = r.i32(); templates.set(id, defineTemplate(r.str())); break; }
        case 18: {
          const t = templates.get(r.i32());
          const first = r.i32();
          const root = clone(t.root);
          nodes.set(first, root);
          t.paths.forEach((p, h) => {
            let n = root;
            for (const i of p) n = n.children[i];
            nodes.set(first + 1 + h, n);
          });
          break;
        }
        default:
          throw new Error(`NetWasm.UI prerender: unknown opcode ${op}`);
      }
    }
  }

  state.host = Object.freeze({
    applyBatch(bytes) {
      state.batches++;
      state.lastBatch = Date.now();
      // An exception here would surface as an anonymous managed error, so keep it for the report.
      try { apply(bytes); } catch (e) { state.error ??= e; throw e; }
    },
    subscribe() { return { dispose() {} }; },
    currentPath: () => path,
    navigate() {},
    onPopState() { return { dispose() {} }; },
    basePath: () => base,
    devState: () => "",
    devConnect() { return { dispose() {} }; },
    devPut() {},
    dispose() {},
  });
  return state;
}

class Reader {
  constructor(bytes) {
    this.bytes = bytes;
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    this.pos = 0;
    this.decoder = new TextDecoder();
  }
  get done() { return this.pos >= this.bytes.length; }
  u8() { return this.bytes[this.pos++]; }
  i32() { const v = this.view.getInt32(this.pos, true); this.pos += 4; return v; }
  str() {
    const len = this.view.getUint32(this.pos, true);
    this.pos += 4;
    const s = this.decoder.decode(this.bytes.subarray(this.pos, this.pos + len));
    this.pos += len;
    return s;
  }
}

// Mirrors defineTemplate in dom.mjs: holes are the root, elements with a `$` attribute, <!--$--> (an empty
// text node) and <!--@--> (an insertion point), in document order. Template text is kept as written.
function defineTemplate(html) {
  const fragment = element("#fragment");
  parseHtml(html, fragment);
  removeBlankText(fragment);
  const first = fragment.children[0];
  if (!first || first.type !== 1 || fragment.children.length !== 1) throw new Error("NetWasm.UI: a template needs exactly one root element");
  first.attrs.delete("$");

  const holes = [];
  (function walk(n) {
    for (const c of n.children) {
      if (c.type === 1) {
        if (c.attrs.has("$")) { c.attrs.delete("$"); holes.push(c); }
        walk(c);
      } else if (c.type === 8 && (c.data === "$" || c.data === "@")) {
        holes.push(c);
      }
    }
  })(first);

  const paths = holes.map(n => {
    const p = [];
    for (; n !== first; n = n.parent) p.unshift(n.parent.children.indexOf(n));
    return p;
  });
  for (const n of holes) {
    if (n.type !== 8) continue;
    if (n.data === "$") { const t = text(""); insertBefore(n, t); detach(n); }
    else n.data = "";
  }
  // Paths were taken before the <!--$--> swaps; the replacement text sits at the same index.
  return { root: first, paths };
}

function removeBlankText(n) {
  if (n.type !== 1) return;
  n.children = n.children.filter(c => !(c.type === 3 && c.data.includes("\n") && c.data.trim() === ""));
  for (const c of n.children) removeBlankText(c);
}

function parseHtml(html, root) {
  let cur = root;
  let i = 0;
  const pushText = s => { if (s) append(cur, text(s, true)); };
  while (i < html.length) {
    const lt = html.indexOf("<", i);
    if (lt < 0) { pushText(html.slice(i)); break; }
    pushText(html.slice(i, lt));
    if (html.startsWith("<!--", lt)) {
      const end = html.indexOf("-->", lt + 4);
      const stop = end < 0 ? html.length : end;
      append(cur, comment(html.slice(lt + 4, stop)));
      i = end < 0 ? html.length : end + 3;
      continue;
    }
    if (html[lt + 1] === "/") {
      const end = html.indexOf(">", lt);
      const tag = html.slice(lt + 2, end < 0 ? html.length : end).trim().toLowerCase();
      for (let n = cur; n && n !== root; n = n.parent) {
        if (n.tag === tag) { cur = n.parent; break; }
      }
      i = end < 0 ? html.length : end + 1;
      continue;
    }
    if (!/[a-zA-Z]/.test(html[lt + 1] ?? "")) { pushText("<"); i = lt + 1; continue; }

    const m = /^<([a-zA-Z][\w:-]*)/.exec(html.slice(lt));
    const el = element(m[1]);
    let p = lt + m[0].length;
    let selfClosing = false;
    for (;;) {
      while (/\s/.test(html[p] ?? "")) p++;
      if (p >= html.length) break;
      if (html[p] === ">") { p++; break; }
      if (html.startsWith("/>", p)) { selfClosing = true; p += 2; break; }
      const nm = /^[^\s"'>/=]+/.exec(html.slice(p));
      if (!nm) { p++; continue; }
      p += nm[0].length;
      while (/\s/.test(html[p] ?? "")) p++;
      let value = "";
      if (html[p] === "=") {
        p++;
        while (/\s/.test(html[p] ?? "")) p++;
        const q = html[p];
        if (q === '"' || q === "'") {
          const end = html.indexOf(q, p + 1);
          value = html.slice(p + 1, end < 0 ? html.length : end);
          p = end < 0 ? html.length : end + 1;
        } else {
          const v = /^[^\s>]*/.exec(html.slice(p))[0];
          value = v;
          p += v.length;
        }
      }
      el.attrs.set(nm[0].toLowerCase(), { value, raw: true });
    }
    append(cur, el);
    i = p;
    if (selfClosing || VOID.has(el.tag)) continue;
    if (RAW_TEXT.has(el.tag) || el.tag === "textarea" || el.tag === "title") {
      const close = html.toLowerCase().indexOf(`</${el.tag}`, i);
      const stop = close < 0 ? html.length : close;
      if (stop > i) append(el, text(html.slice(i, stop), true));
      const gt = close < 0 ? html.length : html.indexOf(">", close);
      i = gt < 0 ? html.length : gt + 1;
      continue;
    }
    cur = el;
  }
}

function escapeText(s) { return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }
function escapeAttr(s) { return s.replace(/&/g, "&amp;").replace(/"/g, "&quot;"); }

function serialize(n) {
  if (n.type === 3) return n.raw || RAW_TEXT.has(n.parent?.tag) ? n.data : escapeText(n.data);
  if (n.type === 8) return `<!--${n.data}-->`;
  let s = "<" + n.tag;
  for (const [k, { value, raw }] of n.attrs) {
    s += value === "" ? " " + k : ` ${k}="${raw ? value.replace(/"/g, "&quot;") : escapeAttr(value)}"`;
  }
  s += ">";
  if (VOID.has(n.tag)) return s;
  for (const c of n.children) s += serialize(c);
  return s + `</${n.tag}>`;
}

// Last, so every class and constant above is initialized before the top-level await runs.
const args = parseArgs(process.argv.slice(2));

if (args.render) {
  await renderRoute(args);
} else {
  process.exitCode = main(args);
}
