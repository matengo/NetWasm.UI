// NetWasm.UI DOM host. Decodes the binary op batch produced by NetWasm.UI.Dom.Renderer
// and applies it to the real DOM. Keep opcodes in sync with src/NetWasm.UI/Dom/Op.cs.
//
// Two ways to use it:
//  - Bundled (default): declare this file as the "nwui.dom" NetWasmJavaScriptModule. The named exports
//    below then drive one default host over `document`.
//  - Per instance: don't bundle it; import this file from the page and pass a host per Wasm instance:
//      const host = createDomHost({ root: element });
//      const app = await open({ modules: { "nwui.dom": host } });
//    Each host keeps its own node table, listeners and templates, delegates events from its own root
//    and tags nodes with its own key, so several apps (or several instances of one app) can share a page.

const OP = {
  CreateElement: 1,
  CreateText: 2,
  CreateAnchor: 3,
  SetText: 4,
  SetAttribute: 5,
  RemoveAttribute: 6,
  SetProperty: 7,
  SetBoolProperty: 8,
  AppendChild: 9,
  InsertBefore: 10,
  Remove: 11,
  Release: 12,
  AddListener: 13,
  RemoveListener: 14,
  Mount: 15,
  Focus: 16,
  DefineTemplate: 17,
  CloneTemplate: 18,
};

const FLAG_PREVENT_DEFAULT = 1;
const FLAG_STOP_PROPAGATION = 2;
const FLAG_PREVENT_DEFAULT_PLAIN_CLICK = 4;

const EV_CTRL = 1, EV_SHIFT = 2, EV_ALT = 4, EV_META = 8, EV_CHECKED = 16, EV_REPEAT = 32;

const decoder = new TextDecoder();
const encoder = new TextEncoder();
const EMPTY = new Uint8Array(0);

// Bubbling events get one listener on the host root per type; the handlers live on the node itself
// (node[key][type] = [handlerId, flags, ...]) and are found by walking up from the target.
// Delegated handlers therefore run after native listeners added directly on ancestors.
const DELEGATED = new Set([
  "click", "dblclick", "contextmenu", "input", "change", "beforeinput", "submit",
  "keydown", "keyup", "mousedown", "mouseup", "pointerdown", "pointerup", "focusin", "focusout",
]);

class Reader {
  constructor(bytes) {
    this.bytes = bytes;
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    this.pos = 0;
  }
  get done() { return this.pos >= this.bytes.length; }
  u8() { return this.bytes[this.pos++]; }
  i32() { const v = this.view.getInt32(this.pos, true); this.pos += 4; return v; }
  str() {
    const len = this.view.getUint32(this.pos, true);
    this.pos += 4;
    const bytes = this.bytes;
    const start = this.pos;
    const end = start + len;
    this.pos = end;
    // Most strings in a batch are short ASCII (ids, labels, attribute and event names): building them
    // directly is several times faster than a TextDecoder call with its subarray.
    if (len <= 32) {
      let s = "";
      for (let i = start; i < end; i++) {
        const c = bytes[i];
        if (c > 0x7f) return decoder.decode(bytes.subarray(start, end));
        s += String.fromCharCode(c);
      }
      return s;
    }
    return decoder.decode(bytes.subarray(start, end));
  }
}

function isPlainClick(e) {
  return (e.button ?? 0) === 0 && !e.ctrlKey && !e.shiftKey && !e.altKey && !e.metaKey;
}

// Encodes the event as the record NetWasm.UI.DomEvent.Decode expects:
// u8 flags, i32 button, i32 clientX, i32 clientY, str key, str value.
function payloadFor(e) {
  const t = e.target;
  let key = "", value = "", flags = 0, button = 0, x = 0, y = 0;

  if (e.ctrlKey) flags |= EV_CTRL;
  if (e.shiftKey) flags |= EV_SHIFT;
  if (e.altKey) flags |= EV_ALT;
  if (e.metaKey) flags |= EV_META;
  if (e.repeat) flags |= EV_REPEAT;

  if (typeof e.button === "number") { button = e.button; x = e.clientX | 0; y = e.clientY | 0; }
  if (typeof e.key === "string") key = e.key;

  if (t && (e.type === "input" || e.type === "change" || e.type === "blur" || e.type === "keydown" || e.type === "keyup")) {
    if (t.type === "checkbox" || t.type === "radio") { if (t.checked) flags |= EV_CHECKED; }
    if ("value" in t) value = String(t.value ?? "");
  }

  if (flags === 0 && button === 0 && x === 0 && y === 0 && key === "" && value === "") return EMPTY;

  const keyBytes = encoder.encode(key);
  const valueBytes = encoder.encode(value);
  const out = new Uint8Array(1 + 12 + 4 + keyBytes.length + 4 + valueBytes.length);
  const view = new DataView(out.buffer);
  let p = 0;
  out[p++] = flags;
  view.setInt32(p, button, true); p += 4;
  view.setInt32(p, x, true); p += 4;
  view.setInt32(p, y, true); p += 4;
  view.setUint32(p, keyBytes.length, true); p += 4;
  out.set(keyBytes, p); p += keyBytes.length;
  view.setUint32(p, valueBytes.length, true); p += 4;
  out.set(valueBytes, p);
  return out;
}

function windowHistory() {
  const currentPath = () => {
    const l = window.location;
    return l.pathname + l.search + l.hash;
  };
  return {
    currentPath,
    navigate(path, replace) {
      if (replace) {
        history.replaceState(null, "", path);
      } else {
        history.pushState(null, "", path);
        // A new page starts at the top, like a normal navigation; links to #fragments keep their position.
        if (!path.includes("#")) window.scrollTo(0, 0);
      }
    },
    onPopState(fn) {
      const handler = () => fn(currentPath());
      window.addEventListener("popstate", handler);
      return { dispose() { window.removeEventListener("popstate", handler); } };
    },
  };
}

// The path of <base href>, without a trailing slash, or "" when the document has no <base>.
function documentBasePath(doc) {
  if (!doc.querySelector("base[href]")) return "";
  const p = new URL(doc.baseURI).pathname;
  return p.endsWith("/") ? p.slice(0, -1) : p;
}

// Trusted Types (CSP `require-trusted-types-for 'script'`): template HTML is the compile-time text
// from the .nwui generator, never user data, so the "nwui" policy passes it through unchanged.
// Allow it with `trusted-types nwui`. Created once, on the first template; null without Trusted Types.
let templatePolicy;
function trustedTemplateHtml(html) {
  if (templatePolicy === undefined) {
    try {
      templatePolicy = globalThis.trustedTypes?.createPolicy("nwui", { createHTML: s => s }) ?? null;
    } catch {
      templatePolicy = null; // The CSP does not allow the "nwui" policy; innerHTML reports the violation.
    }
  }
  return templatePolicy ? templatePolicy.createHTML(html) : html;
}

/**
 * Creates an independent DOM host: the import module "nwui.dom" for one Wasm instance.
 * @param {{ root?: Document | Element, basePath?: string, history?: { currentPath(): string, navigate(path: string, replace: boolean): void, onPopState(fn: (path: string) => void): { dispose(): void } } }} [options]
 *   `root`: where events are delegated from and mount selectors are resolved (default: `document`).
 *   An empty mount selector mounts directly into `root`.
 *   `history`: replaces the browser History API, e.g. when a shell owns the URL.
 *   `basePath`: the path the app is served under (default: the path of `<base href>`, or the site root).
 */
export function createDomHost(options = {}) {
  const root = options.root ?? document;
  const doc = root.nodeType === 9 ? root : root.ownerDocument;
  const hist = options.history ?? windowHistory();
  const key = Symbol("nwui");

  /** @type {Map<number, Node>} */
  const nodes = new Map();
  /** @type {Map<number, { node: Node, event: string, fn: EventListener }>} */
  const listeners = new Map();
  // Handler ids per node, so releasing a node only visits its own listeners.
  /** @type {WeakMap<Node, number[]>} */
  const nodeListeners = new WeakMap();
  /** @type {Map<number, { root: Node, paths: number[][] }>} */
  const templates = new Map();
  const delegatedTypes = new Set();
  const mounted = new Set();

  /** @type {((handlerId: number, payload: Uint8Array) => void) | null} */
  let dispatch = null;
  let rendered = false;
  /** @type {number | undefined} */
  let devId;
  let devCaptured = "";

  // The batch is a view of Wasm memory that is only valid while applyBatch runs. Events the DOM fires
  // synchronously while a batch is applied (focus, blur on removal, ...) are queued and delivered after
  // the batch has been read, so managed code never runs (and never grows memory) mid-batch.
  let applying = false;
  const pending = [];

  function send(handlerId, payload) {
    if (applying) pending.push(handlerId, payload);
    else dispatch?.(handlerId, payload);
  }

  function onDelegated(e) {
    const type = e.type;
    let payload = null;
    for (let n = e.target; n; n = n.parentNode) {
      const list = n[key]?.[type];
      if (list) {
        for (const [handlerId, flags] of list.slice()) {
          if (flags & FLAG_PREVENT_DEFAULT) e.preventDefault();
          if ((flags & FLAG_PREVENT_DEFAULT_PLAIN_CLICK) && isPlainClick(e)) e.preventDefault();
          if (flags & FLAG_STOP_PROPAGATION) e.stopPropagation();
          payload ??= payloadFor(e);
          send(handlerId, payload);
        }
        if (e.cancelBubble) return;
      }
      if (n === root) return;
    }
  }

  function addDelegated(n, event, handlerId, flags) {
    if (!delegatedTypes.has(event)) {
      delegatedTypes.add(event);
      root.addEventListener(event, onDelegated);
    }
    const map = n[key] ??= {};
    (map[event] ??= []).push([handlerId, flags]);
  }

  function removeDelegated(n, handlerId) {
    const map = n?.[key];
    if (!map) return false;
    for (const event in map) {
      const list = map[event];
      const i = list.findIndex(h => h[0] === handlerId);
      if (i >= 0) { list.splice(i, 1); return true; }
    }
    return false;
  }

  // Parses the HTML once. Holes (see NetWasm.UI.Template): the root, elements with a `$` attribute,
  // <!--$--> (becomes an empty text node) and <!--@--> (an insertion point), in document order.
  // The HTML is the compile-time template text from the .nwui generator, never user data.
  function defineTemplate(id, html) {
    const t = doc.createElement("template");
    t.innerHTML = trustedTemplateHtml(html);
    const content = t.content;

    // JSX-like whitespace: text that is only whitespace and spans lines is layout, not content.
    const blank = [];
    const texts = doc.createTreeWalker(content, NodeFilter.SHOW_TEXT);
    while (texts.nextNode()) {
      const d = texts.currentNode.data;
      if (d.includes("\n") && d.trim() === "") blank.push(texts.currentNode);
    }
    for (const n of blank) n.remove();

    const first = content.firstChild;
    if (!first || first.nodeType !== 1 || first.nextSibling) throw new Error("NetWasm.UI: a template needs exactly one root element");
    first.removeAttribute("$");

    const holes = [];
    const walker = doc.createTreeWalker(first, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_COMMENT);
    while (walker.nextNode()) {
      const n = walker.currentNode;
      if (n.nodeType === 1) {
        if (n.hasAttribute("$")) { n.removeAttribute("$"); holes.push(n); }
      } else if (n.data === "$" || n.data === "@") {
        holes.push(n);
      }
    }

    for (let i = 0; i < holes.length; i++) {
      const n = holes[i];
      if (n.nodeType !== 8) continue;
      if (n.data === "$") { const text = doc.createTextNode(""); n.replaceWith(text); holes[i] = text; }
      else n.data = "";
    }

    const paths = holes.map(n => {
      const path = [];
      for (; n !== first; n = n.parentNode) {
        let i = 0;
        for (let s = n.previousSibling; s; s = s.previousSibling) i++;
        path.unshift(i);
      }
      return path;
    });

    templates.set(id, { root: doc.importNode(first, true), paths });
  }

  function cloneTemplate(id, firstId) {
    const t = templates.get(id);
    const clone = t.root.cloneNode(true);
    nodes.set(firstId, clone);
    const paths = t.paths;
    for (let h = 0; h < paths.length; h++) {
      const path = paths[h];
      let n = clone;
      for (let k = 0; k < path.length; k++) {
        n = n.firstChild;
        for (let j = path[k]; j > 0; j--) n = n.nextSibling;
      }
      nodes.set(firstId + 1 + h, n);
    }
  }

  function releaseListeners(node) {
    const ids = nodeListeners.get(node);
    if (!ids) return;
    nodeListeners.delete(node);
    for (const id of ids) {
      const l = listeners.get(id);
      if (l && l.node === node) {
        node.removeEventListener(l.event, l.fn);
        listeners.delete(id);
      }
    }
  }

  function mountTarget(selector) {
    if (selector === "") return root.nodeType === 9 ? root.body : root;
    if (root.nodeType === 1 && root.matches(selector)) return root;
    return root.querySelector(selector);
  }

  function apply(bytes) {
    const r = new Reader(bytes);
    while (!r.done) {
      const op = r.u8();
      switch (op) {
        case OP.CreateElement: {
          const id = r.i32();
          nodes.set(id, doc.createElement(r.str()));
          break;
        }
        case OP.CreateText: {
          const id = r.i32();
          nodes.set(id, doc.createTextNode(r.str()));
          break;
        }
        case OP.CreateAnchor: {
          nodes.set(r.i32(), doc.createComment(""));
          break;
        }
        case OP.SetText: {
          const n = nodes.get(r.i32());
          n.data = r.str();
          break;
        }
        case OP.SetAttribute: {
          const n = nodes.get(r.i32());
          const name = r.str();
          n.setAttribute(name, r.str());
          break;
        }
        case OP.RemoveAttribute: {
          const n = nodes.get(r.i32());
          n.removeAttribute(r.str());
          break;
        }
        case OP.SetProperty: {
          const n = nodes.get(r.i32());
          const name = r.str();
          n[name] = r.str();
          break;
        }
        case OP.SetBoolProperty: {
          const n = nodes.get(r.i32());
          const name = r.str();
          n[name] = r.u8() !== 0;
          break;
        }
        case OP.AppendChild: {
          const parent = nodes.get(r.i32());
          const child = nodes.get(r.i32());
          // A comment parent is an insertion point (template <!--@--> hole): the child goes before it.
          if (parent.nodeType === 8) parent.parentNode.insertBefore(child, parent);
          else parent.appendChild(child);
          break;
        }
        case OP.InsertBefore: {
          const ref = nodes.get(r.i32());
          const child = nodes.get(r.i32());
          ref.parentNode.insertBefore(child, ref);
          break;
        }
        case OP.Remove: {
          const n = nodes.get(r.i32());
          n.parentNode?.removeChild(n);
          mounted.delete(n);
          break;
        }
        case OP.Release: {
          const id = r.i32();
          const n = nodes.get(id);
          if (n) {
            releaseListeners(n);
            if (n[key]) n[key] = undefined;
          }
          nodes.delete(id);
          break;
        }
        case OP.AddListener: {
          const n = nodes.get(r.i32());
          const event = r.str();
          const handlerId = r.i32();
          const flags = r.u8();
          if (DELEGATED.has(event)) {
            addDelegated(n, event, handlerId, flags);
            break;
          }
          const fn = (e) => {
            if (flags & FLAG_PREVENT_DEFAULT) e.preventDefault();
            if ((flags & FLAG_PREVENT_DEFAULT_PLAIN_CLICK) && isPlainClick(e)) e.preventDefault();
            if (flags & FLAG_STOP_PROPAGATION) e.stopPropagation();
            send(handlerId, payloadFor(e));
          };
          n.addEventListener(event, fn);
          listeners.set(handlerId, { node: n, event, fn });
          const ids = nodeListeners.get(n);
          if (ids) ids.push(handlerId);
          else nodeListeners.set(n, [handlerId]);
          break;
        }
        case OP.RemoveListener: {
          const n = nodes.get(r.i32());
          const handlerId = r.i32();
          if (removeDelegated(n, handlerId)) break;
          const l = listeners.get(handlerId);
          if (l) {
            l.node.removeEventListener(l.event, l.fn);
            listeners.delete(handlerId);
          }
          break;
        }
        case OP.Mount: {
          const n = nodes.get(r.i32());
          const selector = r.str();
          const target = mountTarget(selector);
          if (!target) throw new Error(`NetWasm.UI: mount target '${selector}' not found`);
          target.appendChild(n);
          mounted.add(n);
          break;
        }
        case OP.Focus: {
          nodes.get(r.i32())?.focus?.();
          break;
        }
        case OP.DefineTemplate: {
          const id = r.i32();
          defineTemplate(id, r.str());
          break;
        }
        case OP.CloneTemplate: {
          const id = r.i32();
          cloneTemplate(id, r.i32());
          break;
        }
        default:
          throw new Error(`NetWasm.UI: unknown opcode ${op} at ${r.pos - 1}`);
      }
    }
  }

  return Object.freeze({
    applyBatch(bytes) {
      const outer = !applying;
      applying = true;
      try {
        apply(bytes);
      } finally {
        if (outer) applying = false;
      }
      if (outer) {
        while (pending.length > 0) {
          const handlerId = pending.shift();
          const payload = pending.shift();
          dispatch?.(handlerId, payload);
        }
        if (!rendered) {
          rendered = true;
          globalThis.__nwuiDev?.onRendered?.();
        }
      }
    },

    subscribe(fn) {
      dispatch = fn;
      return { dispose() { if (dispatch === fn) dispatch = null; } };
    },

    currentPath: () => hist.currentPath(),
    navigate: (path, replace) => hist.navigate(path, replace),
    onPopState: fn => hist.onPopState(fn),
    basePath: () => options.basePath ?? documentBasePath(doc),

    // Dev-server state across reloads; `globalThis.__nwuiDev` only exists when the reload client runs.
    // Hosts are numbered in start order, so several apps on one page keep their own state.
    devState() {
      const dev = globalThis.__nwuiDev;
      if (!dev) return "";
      devId ??= dev.nextId++;
      return "1" + (dev.restored?.signals?.[devId] ?? "");
    },
    devConnect(fn) {
      const dev = globalThis.__nwuiDev;
      if (!dev || devId === undefined) return { dispose() {} };
      const save = () => {
        devCaptured = "";
        fn(0);
        return devCaptured;
      };
      dev.savers[devId] = save;
      return { dispose() { if (dev.savers[devId] === save) delete dev.savers[devId]; } };
    },
    devPut(state) { devCaptured = state; },

    /** Removes everything this host mounted and all its listeners. Call after the Wasm instance is closed. */
    dispose() {
      const dev = globalThis.__nwuiDev;
      if (dev && devId !== undefined) delete dev.savers[devId];
      for (const type of delegatedTypes) root.removeEventListener(type, onDelegated);
      delegatedTypes.clear();
      for (const l of listeners.values()) l.node.removeEventListener(l.event, l.fn);
      listeners.clear();
      for (const n of mounted) n.parentNode?.removeChild(n);
      mounted.clear();
      nodes.clear();
      templates.clear();
      pending.length = 0;
      dispatch = null;
    },
  });
}

// ---- default host (bundled use) ----

let defaultHost = null;
const host = () => defaultHost ??= createDomHost();

export function applyBatch(bytes) { host().applyBatch(bytes); }
export function subscribe(fn) { return host().subscribe(fn); }
export function currentPath() { return host().currentPath(); }
export function navigate(path, replace) { host().navigate(path, replace); }
export function onPopState(fn) { return host().onPopState(fn); }
export function basePath() { return host().basePath(); }
export function devState() { return host().devState(); }
export function devConnect(fn) { return host().devConnect(fn); }
export function devPut(state) { host().devPut(state); }
