// The [JSImport] module for Interop/MeterJs.cs. Keep it thin: plain values in, plain values out.
let lib = null;

// Promise -> Task: loads the library on demand, like a script from a CDN.
export async function load() {
  lib = await import(new URL("vendor/meter-lib.mjs", document.baseURI).href);
}

// Returns a JS object; C# holds it as a JSObject and must dispose it.
export function create(selector, max) {
  return lib.createMeter(document.querySelector(selector), { max });
}

export function setValue(meter, value) { meter.setValue(value); }

export function destroy(meter) { meter.destroy(); }

// A callback: exactly one delegate parameter and a { dispose() } result (JSSubscription in C#).
export function onChange(meter, fn) {
  meter.onChange = fn;
  return { dispose() { meter.onChange = undefined; } };
}