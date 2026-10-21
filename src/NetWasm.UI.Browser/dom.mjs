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

const decoder = new TextDecoder();

/** @type {Map<number, Node>} */
const nodes = new Map();
/** @type {Map<number, { node: Node, event: string, fn: EventListener }>} */
const listeners = new Map();
/** @type {((handlerId: number, payload: string) => void) | null} */
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

function payloadFor(e) {
  const t = e.target;
  switch (e.type) {
    case "input":
    case "change":
      if (t && t.type === "checkbox") return String(t.checked);
      return t && "value" in t ? String(t.value) : "";
    case "keydown":
    case "keyup":
    case "keypress":
      return e.key ?? "";
    default:
      return "";
  }
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
