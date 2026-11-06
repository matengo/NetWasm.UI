// [JSImport] module "site.interop" (see SiteJs.cs). Plain values in, plain values out.

export function copyText(text) {
  navigator.clipboard?.writeText(text).catch(() => {});
}

// Promise<string> -> Task<string>. Paths are relative to <base href>.
export async function fetchText(path) {
  const response = await fetch(new URL(path, document.baseURI));
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return await response.text();
}

// Compressed body sizes: what a first visit downloads, even when this visit was served from cache.
function bytes(entry) {
  return entry.encodedBodySize || entry.transferSize || 0;
}

function entries() {
  return [...performance.getEntriesByType("navigation"), ...performance.getEntriesByType("resource")];
}

export function pageBytes() {
  return entries().reduce((sum, e) => sum + bytes(e), 0);
}

export function wasmBytes() {
  return entries().filter(e => e.name.endsWith(".wasm")).reduce((sum, e) => sum + bytes(e), 0);
}

export function setTitle(title) {
  document.title = title;
}
