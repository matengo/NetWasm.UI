import { createMemo, createRenderEffect, createRoot, createSignal } from "solid-js";
import { label } from "./data.js";

// Same workloads as benchmarks/NetWasmBench/ReactiveBench.cs. Each returns a checksum the runner compares.
// createRenderEffect runs synchronously, like NetWasm.UI's Effect; updates run outside the root callback.

function deep() {
  let sum = 0;
  const [source, setSource] = createSignal(0);
  const dispose = createRoot(dispose => {
    let current = source;
    for (let i = 0; i < 100; i++) {
      const previous = current;
      current = createMemo(() => previous() + 1);
    }
    const last = current;
    createRenderEffect(() => { sum += last(); });
    return dispose;
  });
  for (let i = 1; i <= 1000; i++) setSource(i);
  dispose();
  return sum | 0;
}

function broad() {
  let sum = 0;
  const [source, setSource] = createSignal(0);
  const dispose = createRoot(dispose => {
    for (let i = 0; i < 1000; i++) {
      const k = i;
      const c = createMemo(() => source() + k);
      createRenderEffect(() => { sum += c(); });
    }
    return dispose;
  });
  for (let i = 1; i <= 100; i++) setSource(i);
  dispose();
  return sum | 0;
}

function diamond() {
  let sum = 0;
  const [source, setSource] = createSignal(0);
  const dispose = createRoot(dispose => {
    const branches = [];
    for (let i = 0; i < 10; i++) {
      const k = i + 1;
      branches.push(createMemo(() => source() * k));
    }
    const total = createMemo(() => {
      let t = 0;
      for (let i = 0; i < branches.length; i++) t += branches[i]();
      return t;
    });
    createRenderEffect(() => { sum += total(); });
    return dispose;
  });
  for (let i = 1; i <= 1000; i++) setSource(i);
  dispose();
  return sum | 0;
}

function create() {
  let sum = 0;
  const setters = new Array(10000);
  const dispose = createRoot(dispose => {
    for (let i = 0; i < setters.length; i++) {
      const [s, set] = createSignal(i);
      const c = createMemo(() => s() * 2);
      createRenderEffect(() => { sum += c(); });
      setters[i] = set;
    }
    return dispose;
  });
  for (let i = 0; i < setters.length; i++) setters[i](i + 1);
  dispose();
  return sum | 0;
}

function sort() {
  const state = { seed: 1 };
  const items = new Array(100000);
  for (let i = 0; i < items.length; i++) items[i] = { id: i, label: label(state) };
  // Plain < compares UTF-16 code units, i.e. the same order as string.CompareOrdinal.
  items.sort((a, b) => (a.label < b.label ? -1 : a.label > b.label ? 1 : a.id - b.id));
  let hash = 0;
  for (let i = 0; i < items.length; i++) hash = (Math.imul(hash, 31) + items[i].id) | 0;
  return hash;
}

function sieve() {
  const n = 2000000;
  const composite = new Uint8Array(n);
  let count = 0;
  for (let i = 2; i < n; i++) {
    if (composite[i]) continue;
    count++;
    for (let j = i * i; j < n; j += i) composite[j] = 1;
  }
  return count;
}

const workloads = { deep, broad, diamond, create, sort, sieve };

export function reactive(name) {
  const fn = workloads[name];
  if (!fn) throw new Error("Unknown workload: " + name);
  return fn();
}
