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
  SetTextInt: 19,
  RemoveRange: 20,
  Call: 21,
  AddGlobalListener: 22,
  RemoveGlobalListener: 23,
  ObserveResize: 24,
  Portal: 25,
  TemplateListener: 26,
  ReleaseBlock: 27,
  RemoveAnimated: 28,
  FocusEdge: 29,
  CreateSvgElement: 30,
  DefineSvgTemplate: 31,
};

const FLAG_PREVENT_DEFAULT = 1;
const FLAG_STOP_PROPAGATION = 2;
const SVG_NS = "http://www.w3.org/2000/svg";
const FLAG_PREVENT_DEFAULT_PLAIN_CLICK = 4;
const FLAG_CAPTURE = 8;
const FLAG_PASSIVE = 16;
const TABBABLE = 'a[href],area[href],button,input:not([type=hidden]),select,textarea,iframe,summary,audio[controls],video[controls],[contenteditable]:not([contenteditable=false]),[tabindex]';
const isTabbable = (el) => el.tabIndex >= 0 && !el.matches(':disabled') && !el.closest('[inert]') &&
  (el.checkVisibility ? el.checkVisibility({ visibilityProperty: true }) : el.getClientRects().length > 0);
// Focuses the first (or last) tabbable descendant of n, or n itself when it has none.
function focusEdge(n, last) {
  if (!n?.querySelectorAll) return;
  const all = n.querySelectorAll(TABBABLE);
  for (let i = last ? all.length - 1 : 0; last ? i >= 0 : i < all.length; i += last ? -1 : 1) {
    if (isTabbable(all[i])) { all[i].focus(); return; }
  }
  n.focus?.();
}
// Marks n closed and detaches it once the (finite) animations and transitions that starts have finished. C# has
// already let go of n, so it is inert meanwhile. Nothing to wait for: it goes at once.
function removeAnimated(n) {
  if (!n?.parentNode) return;
  let running = [];
  if (n.nodeType === 1 && n.getAnimations) {
    n.setAttribute("data-state", "closed");
    n.inert = true;
    running = n.getAnimations({ subtree: true }).filter(a => a.effect?.getComputedTiming().endTime !== Infinity);
  }
  if (running.length === 0) { n.remove(); return; }
  Promise.all(running.map(a => a.finished.catch(() => {}))).then(() => n.remove());
}
const listenOptions = (flags) => (flags & (FLAG_CAPTURE | FLAG_PASSIVE)) ? { capture: !!(flags & FLAG_CAPTURE), passive: !!(flags & FLAG_PASSIVE) } : undefined;

const EV_CTRL = 1, EV_SHIFT = 2, EV_ALT = 4, EV_META = 8, EV_CHECKED = 16, EV_REPEAT = 32, EV_INSIDE = 64, EV_RELATED_INSIDE = 128;

const decoder = new TextDecoder();
const encoder = new TextEncoder();
const EMPTY = new Uint8Array(0);

// Bubbling events get one listener on the host root per type; the handlers live on the node itself
// (node[key][type] = [handlerId, flags, ...]) and are found by walking up from the target.
// Delegated handlers therefore run after native listeners added directly on ancestors.
// Template.CanListen and the template compiler (TemplateEmitter.DelegatedEvents) have the same list; a unit test
// compares the three.
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
  /** Starts reading another batch. */
  reset(bytes) {
    this.bytes = bytes;
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    this.pos = 0;
    return this;
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

function containsNode(scope, n) {
  return !!(scope && n && typeof n.nodeType === "number" && scope.contains(n));
}

// Encodes the event as the record NetWasm.UI.DomEvent.Decode expects:
// u8 flags, i32 button, i32 clientX, i32 clientY, str key, str value, then, only when one is set,
// i32 targetId, i32 relatedTargetId, str newState. `idKey` reads a node's renderer id; `scope` is the node the
// listener is on (for relatedTarget containment); `within` is the element of a document/window listener.
function payloadFor(e, idKey, scope, within) {
  const t = e.target;
  let key = "", value = "", flags = 0, button = 0, x = 0, y = 0;
  const targetId = (idKey && t?.[idKey]) | 0;
  const related = e.relatedTarget;
  const relatedId = (idKey && related?.[idKey]) | 0;
  const newState = typeof e.newState === "string" ? e.newState : "";

  if (within && containsNode(within, t)) flags |= EV_INSIDE;
  if (related && containsNode(within ?? scope, related)) flags |= EV_RELATED_INSIDE;
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

  const extended = targetId !== 0 || relatedId !== 0 || newState !== "";
  if (!extended && flags === 0 && button === 0 && x === 0 && y === 0 && key === "" && value === "") return EMPTY;

  const keyBytes = encoder.encode(key);
  const valueBytes = encoder.encode(value);
  const stateBytes = extended ? encoder.encode(newState) : EMPTY;
  const out = new Uint8Array(1 + 12 + 4 + keyBytes.length + 4 + valueBytes.length + (extended ? 12 + stateBytes.length : 0));
  const view = new DataView(out.buffer);
  let p = 0;
  out[p++] = flags;
  view.setInt32(p, button, true); p += 4;
  view.setInt32(p, x, true); p += 4;
  view.setInt32(p, y, true); p += 4;
  view.setUint32(p, keyBytes.length, true); p += 4;
  out.set(keyBytes, p); p += keyBytes.length;
  view.setUint32(p, valueBytes.length, true); p += 4;
  out.set(valueBytes, p); p += valueBytes.length;
  if (extended) {
    view.setInt32(p, targetId, true); p += 4;
    view.setInt32(p, relatedId, true); p += 4;
    view.setUint32(p, stateBytes.length, true); p += 4;
    out.set(stateBytes, p);
  }
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
  // The renderer's id of an element, so events can report their target and focus reads can answer with an id.
  const idKey = Symbol("nwui.id");
  // An element of a template clone that listens through the template (OP.TemplateListener): the listeners of
  // its hole, one object shared by every clone.
  const rowKey = Symbol("nwui.row");

  // Node ids are small integers that the renderer reuses once released, so an array indexes them without hashing
  // and stays dense however long the app runs.
  /** @type {(Node | undefined)[]} */
  const nodes = [];
  /** @type {Map<number, { node: Node, event: string, fn: EventListener }>} */
  const listeners = new Map();
  // Handler ids per node, so releasing a node only visits its own listeners.
  /** @type {WeakMap<Node, number[]>} */
  const nodeListeners = new WeakMap();
  /** @type {Map<number, { root: Node, paths: number[][] }>} */
  const templates = new Map();
  const delegatedTypes = new Set();
  const mounted = new Set();
  /** @type {Map<number, { target: EventTarget, event: string, fn: EventListener }>} */
  const globals = new Map();
  /** @type {ResizeObserver | null} */
  let resizeObserver = null;
  /** @type {Map<number, Element>} */
  const resized = new Map();
  // The event whose handler is running: a document/window listener added meanwhile must not see that same event.
  let dispatching = null;

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

  function send(handlerId, payload, e) {
    if (applying) { pending.push(handlerId, payload); return; }
    const previous = dispatching;
    dispatching = e ?? previous;
    try {
      dispatch?.(handlerId, payload);
    } finally {
      dispatching = previous;
    }
  }

  function sendRow(first, slot, payload, e) {
    const out = new Uint8Array(5 + payload.length);
    out[0] = first; out[1] = first >> 8; out[2] = first >> 16; out[3] = first >> 24;
    out[4] = slot;
    out.set(payload, 5);
    send(0, out, e);
  }

  function onDelegated(e) {
    settleEvent(e);
    const type = e.type;
    let payload = null;
    // relatedTarget containment depends on the listener's node, so such payloads are built per level.
    const perNode = !!e.relatedTarget;
    for (let n = e.target; n; n = n.parentNode) {
      const list = n[key]?.[type];
      if (list) {
        if (perNode) payload = null;
        for (const [handlerId, flags] of list.slice()) {
          if (flags & FLAG_PREVENT_DEFAULT) e.preventDefault();
          if ((flags & FLAG_PREVENT_DEFAULT_PLAIN_CLICK) && isPlainClick(e)) e.preventDefault();
          if (flags & FLAG_STOP_PROPAGATION) e.stopPropagation();
          payload ??= payloadFor(e, idKey, n, null);
          send(handlerId, payload, e);
        }
        if (e.cancelBubble) return;
      }
      // Template listeners: the row is the clone this hole belongs to, named by its first node id.
      const rows = n[rowKey]?.[type];
      if (rows) {
        if (perNode) payload = null;
        for (let i = 0; i < rows.length; i++) {
          const [slot, flags, hole] = rows[i];
          if (flags & FLAG_PREVENT_DEFAULT) e.preventDefault();
          if ((flags & FLAG_PREVENT_DEFAULT_PLAIN_CLICK) && isPlainClick(e)) e.preventDefault();
          if (flags & FLAG_STOP_PROPAGATION) e.stopPropagation();
          payload ??= payloadFor(e, idKey, n, null);
          sendRow(n[idKey] - hole, slot, payload, e);
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
  // SVG content (Template.Svg) is parsed inside an <svg> element, so the parser puts it in the SVG namespace (and a
  // <foreignObject> back in HTML); the wrapper is then dropped.
  function defineTemplate(id, html, svg) {
    const t = doc.createElement("template");
    t.innerHTML = trustedTemplateHtml(svg ? `<svg>${html}</svg>` : html);
    const content = t.content;
    if (svg) {
      // An HTML element such as <div> ends the parser's SVG content, so it lands outside the wrapper.
      if (content.childNodes.length !== 1) throw new Error("NetWasm.UI: inside <svg>, a block's root must be an SVG element; put HTML in a <foreignObject>");
      content.replaceChildren(...content.firstChild.childNodes);
    }

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
    let custom = first.localName.includes("-") || first.hasAttribute("is");
    while (walker.nextNode()) {
      const n = walker.currentNode;
      if (n.nodeType === 1) {
        custom ||= n.localName.includes("-") || n.hasAttribute("is");
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

    // Clones are made from the parsed template itself: a clone then stays in the template's inert document
    // until it is inserted, which costs the browser less than cloning a node of this document. A template with
    // custom elements is imported first, so that its clones are created upgraded and a property set on them
    // before they are inserted reaches the element's own setter.
    // `shape` is one node shaped like the root of every clone once it has its id (see `reader` below for why the
    // shape has to stay alive). It is cloned from the inert template, so it never runs a custom element's code.
    const shape = first.cloneNode(false);
    shape[idKey] = 0;
    templates.set(id, { root: custom ? doc.importNode(first, true) : first, paths, rows: null, shape });
  }

  // A clone's text holes are found when something first writes to them. Until then a hole's entry in `nodes`
  // is the clone's first id (a number), and `cloneOf` has the clone's template. A list of thousands of rows
  // then walks only to the text it sets, and tags no element it never needs by id. Asking for any other hole
  // (an element or an insertion point) finds all holes of the clone at once: what is done with those can
  // change the clone's structure, and a hole's path is only valid while the structure is the template's.
  const cloneOf = [];

  function cloneTemplate(id, firstId) {
    const t = templates.get(id);
    const clone = t.root.cloneNode(true);
    nodes[firstId] = clone;
    clone[idKey] = firstId;
    const count = t.paths.length;
    if (count > 0 || t.rows) cloneOf[firstId] = t;
    for (let h = 1; h <= count; h++) nodes[firstId + h] = firstId;
  }

  function node(id) {
    const n = nodes[id];
    return typeof n === "number" ? resolveHole(id, n) : n;
  }

  function walk(clone, path) {
    let n = clone;
    for (let k = 0; k < path.length; k++) {
      n = n.firstChild;
      for (let j = path[k]; j > 0; j--) n = n.nextSibling;
    }
    return n;
  }

  function resolveHole(id, firstId) {
    const n = walk(nodes[firstId], cloneOf[firstId].paths[id - firstId - 1]);
    if (n.nodeType !== 3) {
      settleClone(firstId);
      return nodes[id];
    }
    nodes[id] = n;
    return n;
  }

  // Finds every hole of a clone that is still unresolved and gives its elements their ids and the listeners
  // of their template.
  function settleClone(firstId) {
    const t = cloneOf[firstId];
    cloneOf[firstId] = undefined;
    const clone = nodes[firstId];
    const paths = t.paths;
    for (let h = 0; h < paths.length; h++) {
      const id = firstId + 1 + h;
      if (typeof nodes[id] !== "number") continue;
      const n = walk(clone, paths[h]);
      nodes[id] = n;
      if (n.nodeType === 1) n[idKey] = id;
    }
    const rows = t.rows;
    if (rows) {
      for (let h = 0; h < rows.length; h++) {
        if (rows[h]) nodes[firstId + h][rowKey] = rows[h];
      }
    }
  }

  // Before an event is dispatched or a node is asked for its id: every clone around the node is settled, so
  // its elements carry their ids and the listeners of their template.
  function settle(n) {
    for (; n; n = n.parentNode) {
      const id = n[idKey];
      if (id !== undefined && cloneOf[id] && nodes[id] === n) settleClone(id);
      if (n === root) return;
    }
  }

  function settleEvent(e) {
    settle(e.target);
    if (e.relatedTarget) settle(e.relatedTarget);
  }

  function addGlobal(target, event, handlerId, flags, within) {
    const addedAt = performance.now();
    const skip = dispatching;
    const fn = (e) => {
      if (e === skip || e.timeStamp < addedAt) return;
      if (flags & FLAG_PREVENT_DEFAULT) e.preventDefault();
      if ((flags & FLAG_PREVENT_DEFAULT_PLAIN_CLICK) && isPlainClick(e)) e.preventDefault();
      if (flags & FLAG_STOP_PROPAGATION) e.stopPropagation();
      settleEvent(e);
      send(handlerId, payloadFor(e, idKey, null, within), e);
    };
    target.addEventListener(event, fn, listenOptions(flags));
    globals.set(handlerId, { target, event, fn, capture: !!(flags & FLAG_CAPTURE) });
  }

  function observeResize(n, handlerId) {
    resizeObserver ??= new ResizeObserver(entries => {
      for (const entry of entries) {
        for (const [handlerId, node] of resized) {
          if (node === entry.target) send(handlerId, EMPTY);
        }
      }
    });
    resized.set(handlerId, n);
    resizeObserver.observe(n);
  }

  function removeGlobal(handlerId) {
    const g = globals.get(handlerId);
    if (g) {
      g.target.removeEventListener(g.event, g.fn, g.capture);
      globals.delete(handlerId);
      return;
    }
    const n = resized.get(handlerId);
    if (n) {
      resized.delete(handlerId);
      let observed = false;
      for (const other of resized.values()) observed ||= other === n;
      if (!observed) resizeObserver?.unobserve(n);
    }
  }

  // Element.Call: a DOM method with string arguments. A failing call (showModal on an open dialog...) is reported
  // without aborting the rest of the batch.
  function callMethod(n, method, args) {
    if (!n) return;
    try {
      if (method === "scrollIntoView" && args.length === 1) n.scrollIntoView({ block: args[0] });
      else if (typeof n[method] === "function") n[method](...args);
      else console.error(`NetWasm.UI: '${method}' is not a method of <${n.nodeName?.toLowerCase()}>`);
    } catch (err) {
      console.error(`NetWasm.UI: ${method}() failed`, err);
    }
  }

  function releaseListeners(node) {
    const ids = nodeListeners.get(node);
    if (!ids) return;
    nodeListeners.delete(node);
    for (const id of ids) {
      const l = listeners.get(id);
      if (l && l.node === node) {
        node.removeEventListener(l.event, l.fn, l.capture);
        listeners.delete(id);
      }
    }
  }

  function release(id) {
    const n = nodes[id];
    if (cloneOf[id]) cloneOf[id] = undefined;
    // A hole nothing asked for is still the number of its clone.
    if (n && typeof n === "object") {
      releaseListeners(n);
      if (n[key]) n[key] = undefined;
      if (n[rowKey]) n[rowKey] = undefined;
      if (n[idKey] === id) n[idKey] = undefined;
    }
    nodes[id] = undefined;
  }

  function mountTarget(selector) {
    if (selector === "") return root.nodeType === 9 ? root.body : root;
    if (root.nodeType === 1 && root.matches(selector)) return root;
    return root.querySelector(selector);
  }

  // The browser compiles the loop below and what it calls for the shapes of the objects they see. A shape that
  // only short-lived objects have is freed by the browser's next full collection, and the code compiled for it is
  // thrown away with it: a reader created per batch, or the clones of a template after a list was cleared. The
  // next batch then runs in the browser's slowest tiers while it compiles the loop again (about a millisecond for
  // 1,000 rows). So one reader reads every batch, and each template keeps one node shaped like its clones' root.
  const reader = new Reader(EMPTY);

  function apply(bytes) {
    // A batch applied while another one is (it cannot happen today) gets a reader of its own.
    const r = reader.bytes === EMPTY ? reader.reset(bytes) : new Reader(bytes);
    try {
      read(r);
    } finally {
      // Holds no view of the module's memory between batches.
      if (r === reader) reader.reset(EMPTY);
    }
  }

  function read(r) {
    while (!r.done) {
      const op = r.u8();
      switch (op) {
        case OP.CreateElement:
        case OP.CreateSvgElement: {
          const id = r.i32();
          const tag = r.str();
          const n = op === OP.CreateElement ? doc.createElement(tag) : doc.createElementNS(SVG_NS, tag);
          n[idKey] = id;
          nodes[id] = n;
          break;
        }
        case OP.CreateText: {
          const id = r.i32();
          nodes[id] = doc.createTextNode(r.str());
          break;
        }
        case OP.CreateAnchor: {
          nodes[r.i32()] = doc.createComment("");
          break;
        }
        case OP.SetText: {
          const n = node(r.i32());
          n.data = r.str();
          break;
        }
        case OP.SetTextInt: {
          const n = node(r.i32());
          n.data = "" + r.i32();
          break;
        }
        case OP.SetAttribute: {
          const n = node(r.i32());
          const name = r.str();
          n.setAttribute(name, r.str());
          break;
        }
        case OP.RemoveAttribute: {
          const n = node(r.i32());
          n.removeAttribute(r.str());
          break;
        }
        case OP.SetProperty: {
          const n = node(r.i32());
          const name = r.str();
          n[name] = r.str();
          break;
        }
        case OP.SetBoolProperty: {
          const n = node(r.i32());
          const name = r.str();
          n[name] = r.u8() !== 0;
          break;
        }
        case OP.AppendChild: {
          const parent = node(r.i32());
          const child = node(r.i32());
          // A comment parent is an insertion point (template <!--@--> hole): the child goes before it.
          if (parent.nodeType === 8) parent.parentNode.insertBefore(child, parent);
          else parent.appendChild(child);
          break;
        }
        case OP.InsertBefore: {
          const ref = node(r.i32());
          const child = node(r.i32());
          ref.parentNode.insertBefore(child, ref);
          break;
        }
        case OP.Remove: {
          const n = node(r.i32());
          n.parentNode?.removeChild(n);
          mounted.delete(n);
          break;
        }
        case OP.RemoveAnimated: {
          const n = node(r.i32());
          mounted.delete(n);
          removeAnimated(n);
          break;
        }
        case OP.RemoveRange: {
          const first = node(r.i32());
          const end = node(r.i32());
          const parent = first.parentNode;
          if (!first.previousSibling && end.parentNode === parent && !end.nextSibling) {
            // The range is everything but the end marker (a list that is its parent's only content).
            parent.textContent = "";
            parent.appendChild(end);
          } else {
            const range = doc.createRange();
            range.setStartBefore(first);
            range.setEndBefore(end);
            range.deleteContents();
          }
          break;
        }
        case OP.Release: {
          release(r.i32());
          break;
        }
        case OP.ReleaseBlock: {
          const first = r.i32();
          const end = first + r.i32();
          for (let id = first; id < end; id++) release(id);
          break;
        }
        case OP.AddListener: {
          const n = node(r.i32());
          const event = r.str();
          const handlerId = r.i32();
          const flags = r.u8();
          if (DELEGATED.has(event) && !(flags & (FLAG_CAPTURE | FLAG_PASSIVE))) {
            addDelegated(n, event, handlerId, flags);
            break;
          }
          const fn = (e) => {
            if (flags & FLAG_PREVENT_DEFAULT) e.preventDefault();
            if ((flags & FLAG_PREVENT_DEFAULT_PLAIN_CLICK) && isPlainClick(e)) e.preventDefault();
            if (flags & FLAG_STOP_PROPAGATION) e.stopPropagation();
            settleEvent(e);
            send(handlerId, payloadFor(e, idKey, n, null), e);
          };
          n.addEventListener(event, fn, listenOptions(flags));
          listeners.set(handlerId, { node: n, event, fn, capture: !!(flags & FLAG_CAPTURE) });
          const ids = nodeListeners.get(n);
          if (ids) ids.push(handlerId);
          else nodeListeners.set(n, [handlerId]);
          break;
        }
        case OP.RemoveListener: {
          const n = node(r.i32());
          const handlerId = r.i32();
          if (removeDelegated(n, handlerId)) break;
          const l = listeners.get(handlerId);
          if (l) {
            l.node.removeEventListener(l.event, l.fn, l.capture);
            listeners.delete(handlerId);
          }
          break;
        }
        case OP.Mount: {
          const n = node(r.i32());
          const selector = r.str();
          const target = mountTarget(selector);
          if (!target) throw new Error(`NetWasm.UI: mount target '${selector}' not found`);
          // Publish-time prerendered HTML (NetWasmUIPrerender) is replaced in the same task: no blank frame.
          if (target.nodeType === 1 && target.hasAttribute("data-nwui-prerender")) {
            target.removeAttribute("data-nwui-prerender");
            target.replaceChildren(n);
          } else {
            target.appendChild(n);
          }
          mounted.add(n);
          break;
        }
        case OP.Focus: {
          node(r.i32())?.focus?.();
          break;
        }
        case OP.FocusEdge: {
          const n = node(r.i32());
          focusEdge(n, r.u8() === 1);
          break;
        }
        case OP.Call: {
          const n = node(r.i32());
          const method = r.str();
          const count = r.u8();
          const args = [];
          for (let i = 0; i < count; i++) args.push(r.str());
          callMethod(n, method, args);
          break;
        }
        case OP.AddGlobalListener: {
          const which = r.u8();
          const event = r.str();
          const handlerId = r.i32();
          const flags = r.u8();
          const withinId = r.i32();
          addGlobal(which === 1 ? doc.defaultView : doc, event, handlerId, flags, withinId ? node(withinId) ?? null : null);
          break;
        }
        case OP.RemoveGlobalListener: {
          removeGlobal(r.i32());
          break;
        }
        case OP.ObserveResize: {
          const n = node(r.i32());
          const handlerId = r.i32();
          if (n && typeof ResizeObserver === "function") observeResize(n, handlerId);
          break;
        }
        case OP.Portal: {
          const n = node(r.i32());
          const selector = r.str();
          const target = mountTarget(selector);
          if (!target) throw new Error(`NetWasm.UI: portal target '${selector}' not found`);
          target.appendChild(n);
          mounted.add(n);
          break;
        }
        case OP.DefineTemplate:
        case OP.DefineSvgTemplate: {
          const id = r.i32();
          defineTemplate(id, r.str(), op === OP.DefineSvgTemplate);
          break;
        }
        case OP.CloneTemplate: {
          const id = r.i32();
          cloneTemplate(id, r.i32());
          break;
        }
        case OP.TemplateListener: {
          const t = templates.get(r.i32());
          const hole = r.i32();
          const event = r.str();
          const slot = r.u8();
          const flags = r.u8();
          if (!DELEGATED.has(event)) throw new Error(`NetWasm.UI: a template cannot listen for '${event}'`);
          if (!delegatedTypes.has(event)) {
            delegatedTypes.add(event);
            root.addEventListener(event, onDelegated);
          }
          (((t.rows ??= [])[hole] ??= {})[event] ??= []).push([slot, flags, hole]);
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

    // Synchronous reads (NetWasm.UI.Dom.IDomQueries) and element handles (BrowserDom.GetHandle).
    // double[] results must be Float64Arrays: NetWasm rejects plain arrays.
    nodeById: id => node(id) ?? null,
    getRect(id) {
      const n = node(id);
      if (!n || typeof n.getBoundingClientRect !== "function") return new Float64Array(4);
      const b = n.getBoundingClientRect();
      return Float64Array.of(b.x, b.y, b.width, b.height);
    },
    activeElementId() {
      let a = doc.activeElement;
      while (a?.shadowRoot?.activeElement) a = a.shadowRoot.activeElement;
      settle(a);
      return (a?.[idKey]) | 0;
    },
    contains(parentId, childId) {
      const p = node(parentId), c = node(childId);
      return !!(p && c && p.contains(c));
    },
    viewport() {
      const w = doc.defaultView;
      return Float64Array.of(0, 0, w?.innerWidth ?? 0, w?.innerHeight ?? 0);
    },

    /** Removes everything this host mounted and all its listeners. Call after the Wasm instance is closed. */
    dispose() {
      const dev = globalThis.__nwuiDev;
      if (dev && devId !== undefined) delete dev.savers[devId];
      for (const type of delegatedTypes) root.removeEventListener(type, onDelegated);
      delegatedTypes.clear();
      for (const l of listeners.values()) l.node.removeEventListener(l.event, l.fn, l.capture);
      listeners.clear();
      for (const g of globals.values()) g.target.removeEventListener(g.event, g.fn, g.capture);
      globals.clear();
      resizeObserver?.disconnect();
      resizeObserver = null;
      resized.clear();
      for (const n of mounted) n.parentNode?.removeChild(n);
      mounted.clear();
      nodes.length = 0;
      templates.clear();
      pending.length = 0;
      dispatch = null;
    },
  });
}

// ---- default host (bundled use) ----

let defaultHost = null;
// The publish-time prerenderer (build/prerender.mjs) runs the app in Node and supplies its own host here.
const host = () => defaultHost ??= (globalThis.__nwuiPrerenderHost ?? createDomHost());

export function applyBatch(bytes) { host().applyBatch(bytes); }
export function subscribe(fn) { return host().subscribe(fn); }
export function currentPath() { return host().currentPath(); }
export function navigate(path, replace) { host().navigate(path, replace); }
export function onPopState(fn) { return host().onPopState(fn); }
export function basePath() { return host().basePath(); }
export function devState() { return host().devState(); }
export function devConnect(fn) { return host().devConnect(fn); }
export function devPut(state) { host().devPut(state); }
export function nodeById(id) { return host().nodeById(id); }
export function getRect(id) { return host().getRect(id); }
export function activeElementId() { return host().activeElementId(); }
export function contains(parentId, childId) { return host().contains(parentId, childId); }
export function viewport() { return host().viewport(); }
