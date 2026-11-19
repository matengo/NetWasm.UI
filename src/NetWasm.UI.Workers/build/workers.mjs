// NetWasm.UI typed workers, page side ("nwui.workers"). Generated proxies stage their arguments with the
// arg* functions and then call invoke*: staging and invoking are synchronous, so the arguments of one call
// cannot mix with another's. A worker starts on its first call and again on the next call after terminate().
const workers = new Map();
let nextHandle = 1;
let staged = [];
let value;
let dispatch = null;

function baseUri() {
  return globalThis.document?.baseURI ?? globalThis.location?.href;
}

function clientUrl(name) {
  return name.endsWith(".mjs") ? name : `browser/workers/${name}/${name}.worker-client.mjs`;
}

function deliver({ arguments: args }) {
  // [callId, channel, value]: the value is taken by C# (take*) while the callback runs.
  value = args[2];
  try {
    dispatch?.(args[0], args[1]);
  } finally {
    value = undefined;
  }
}

function client(handle) {
  const w = workers.get(handle);
  if (!w) return Promise.reject(new Error("The worker was disposed."));
  if (w.client === null) {
    if (typeof Worker === "undefined") {
      // Prerendering (Node) or another host without Web Workers: the call stays pending, like a load in progress.
      return new Promise(() => {});
    }
    w.client = import(new URL(clientUrl(w.name), baseUri()).href)
      .then(m => m.createWorker({ onNotification: deliver }));
    w.client.catch(() => { if (workers.get(handle) === w) w.client = null; });
  }
  return w.client;
}

async function call(handle, op, callId, args) {
  const c = await client(handle);
  return c[op](callId, ...args);
}

function take() {
  const args = staged;
  staged = [];
  return args;
}

export function create(name) {
  const handle = nextHandle++;
  workers.set(handle, { name, client: null });
  return handle;
}

export function subscribe(fn) {
  dispatch = fn;
  return { dispose() { if (dispatch === fn) dispatch = null; } };
}

export function hardwareConcurrency() {
  return globalThis.navigator?.hardwareConcurrency ?? 1;
}

function push(v) { staged.push(v); }
export const argBool = push, argI32 = push, argI64 = push, argF32 = push, argF64 = push, argStr = push;
export const argBytes = push, argI32Array = push, argF32Array = push, argF64Array = push;

function invoke(handle, op, callId) { return call(handle, op, callId, take()); }
export async function invokeVoid(handle, op, callId) { await invoke(handle, op, callId); }
export const invokeBool = invoke, invokeI32 = invoke, invokeI64 = invoke, invokeF32 = invoke, invokeF64 = invoke, invokeStr = invoke;
export const invokeBytes = invoke, invokeI32Array = invoke, invokeF32Array = invoke, invokeF64Array = invoke;

function current() { return value; }
export const takeBool = current, takeI32 = current, takeI64 = current, takeF32 = current, takeF64 = current, takeStr = current;
export const takeBytes = current, takeI32Array = current, takeF32Array = current, takeF64Array = current;

function send(handle, op, callId, n) {
  const w = workers.get(handle);
  // Only a running worker can have the call; a worker that has not started has nothing to cancel.
  w?.client?.then(c => c[op](callId, n)).catch(() => {});
}

export function cancel(handle, callId) { send(handle, "__nwui_cancel", callId, 0); }
export function credit(handle, callId, n) { send(handle, "__nwui_credit", callId, n); }

export function terminate(handle) {
  const w = workers.get(handle);
  if (!w) return;
  const c = w.client;
  w.client = null;
  c?.then(x => x.terminate(), () => {});
}

export function dispose(handle) {
  const w = workers.get(handle);
  if (!w) return;
  workers.delete(handle);
  w.client?.then(x => x.dispose(), () => {}).catch(() => {});
}
