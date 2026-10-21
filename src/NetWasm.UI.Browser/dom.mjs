// NetWasm.UI DOM host. Decodes the binary op batch produced by NetWasm.UI.Dom.Renderer
// and applies it to the real DOM. Keep opcodes in sync with src/NetWasm.UI/Dom/Op.cs.

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
};

const FLAG_PREVENT_DEFAULT = 1;
const FLAG_STOP_PROPAGATION = 2;
const FLAG_PREVENT_DEFAULT_PLAIN_CLICK = 4;

const EV_CTRL = 1, EV_SHIFT = 2, EV_ALT = 4, EV_META = 8, EV_CHECKED = 16, EV_REPEAT = 32;

const decoder = new TextDecoder();
const encoder = new TextEncoder();
const EMPTY = new Uint8Array(0);

/** @type {Map<number, Node>} */
const nodes = new Map();
/** @type {Map<number, { node: Node, event: string, fn: EventListener }>} */
const listeners = new Map();
/** @type {((handlerId: number, payload: Uint8Array) => void) | null} */
let dispatch = null;

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
    const s = decoder.decode(this.bytes.subarray(this.pos, this.pos + len));
    this.pos += len;
    return s;
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

function releaseListeners(node) {
  for (const [id, l] of listeners) {
    if (l.node === node) {
      node.removeEventListener(l.event, l.fn);
      listeners.delete(id);
    }
  }
}

export function applyBatch(bytes) {
  const r = new Reader(bytes);
  while (!r.done) {
    const op = r.u8();
    switch (op) {
      case OP.CreateElement: {
        const id = r.i32();
        nodes.set(id, document.createElement(r.str()));
        break;
      }
      case OP.CreateText: {
        const id = r.i32();
        nodes.set(id, document.createTextNode(r.str()));
        break;
      }
      case OP.CreateAnchor: {
        nodes.set(r.i32(), document.createComment(""));
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
        parent.appendChild(nodes.get(r.i32()));
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
        break;
      }
      case OP.Release: {
        const id = r.i32();
        const n = nodes.get(id);
        if (n) releaseListeners(n);
        nodes.delete(id);
        break;
      }
      case OP.AddListener: {
        const n = nodes.get(r.i32());
        const event = r.str();
        const handlerId = r.i32();
        const flags = r.u8();
        const fn = (e) => {
          if (flags & FLAG_PREVENT_DEFAULT) e.preventDefault();
          if ((flags & FLAG_PREVENT_DEFAULT_PLAIN_CLICK) && isPlainClick(e)) e.preventDefault();
          if (flags & FLAG_STOP_PROPAGATION) e.stopPropagation();
          dispatch?.(handlerId, payloadFor(e));
        };
        n.addEventListener(event, fn);
        listeners.set(handlerId, { node: n, event, fn });
        break;
      }
      case OP.RemoveListener: {
        r.i32(); // node id (listener map is keyed by handler id)
        const handlerId = r.i32();
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
        const target = document.querySelector(selector);
        if (!target) throw new Error(`NetWasm.UI: mount target '${selector}' not found`);
        target.appendChild(n);
        break;
      }
      case OP.Focus: {
        nodes.get(r.i32())?.focus?.();
        break;
      }
      default:
        throw new Error(`NetWasm.UI: unknown opcode ${op} at ${r.pos - 1}`);
    }
  }
}

export function subscribe(fn) {
  dispatch = fn;
  return { dispose() { if (dispatch === fn) dispatch = null; } };
}

// ---- history (used by NetWasm.UI.Browser.BrowserHistory) ----

export function currentPath() {
  const l = window.location;
  return l.pathname + l.search + l.hash;
}

export function navigate(path, replace) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
}

export function onPopState(fn) {
  const handler = () => fn(currentPath());
  window.addEventListener("popstate", handler);
  return { dispose() { window.removeEventListener("popstate", handler); } };
}
