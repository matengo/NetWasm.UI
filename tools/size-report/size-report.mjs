// Reports the raw, gzip and Brotli sizes of a published NetWasm app and checks them against a budget.
//
// usage: node tools/size-report/size-report.mjs <publish-dir> [--budget <file.json>] [--name <label>]
//                                                [--markdown <file>] [--write-budget <file.json>]
//
// The budget is { "<name>": { "wasmBrotli": n, "totalBrotli": n } }: the Brotli size of the app's
// .wasm and of all .wasm + .mjs + .js files. It fails (exit 1) when a size exceeds its budget.
// --write-budget records the current sizes plus 5% headroom.
import { readdir, readFile, writeFile, appendFile } from "node:fs/promises";
import { join, relative, extname } from "node:path";
import { brotliCompressSync, gzipSync, constants } from "node:zlib";

const args = process.argv.slice(2);
const option = name => {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : undefined;
};
const dir = args.find((a, i) => !a.startsWith("--") && (i === 0 || !args[i - 1].startsWith("--")));
if (!dir) {
  console.error("usage: size-report.mjs <publish-dir> [--budget file] [--name label] [--markdown file] [--write-budget file]");
  process.exit(2);
}
const name = option("--name") ?? "app";

async function* files(root) {
  for (const entry of await readdir(root, { withFileTypes: true })) {
    const path = join(root, entry.name);
    if (entry.isDirectory()) yield* files(path);
    else if ([".wasm", ".mjs", ".js"].includes(extname(entry.name))) yield path;
  }
}

const rows = [];
for await (const path of files(dir)) {
  const bytes = await readFile(path);
  rows.push({
    file: relative(dir, path).replaceAll("\\", "/"),
    raw: bytes.length,
    gzip: gzipSync(bytes, { level: 9 }).length,
    brotli: brotliCompressSync(bytes, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length,
  });
}
rows.sort((a, b) => b.brotli - a.brotli);
const wasm = rows.filter(r => r.file.endsWith(".wasm"));
const sizes = {
  wasmBrotli: wasm.reduce((s, r) => s + r.brotli, 0),
  totalBrotli: rows.reduce((s, r) => s + r.brotli, 0),
};

const table = [
  `### ${name} publish sizes`,
  "| File | Raw | gzip -9 | Brotli 11 |",
  "|---|---:|---:|---:|",
  ...rows.map(r => `| ${r.file} | ${r.raw} | ${r.gzip} | ${r.brotli} |`),
  `| **Total** | ${rows.reduce((s, r) => s + r.raw, 0)} | ${rows.reduce((s, r) => s + r.gzip, 0)} | **${sizes.totalBrotli}** |`,
  "",
];

let failed = false;
const budgetPath = option("--budget");
if (budgetPath) {
  const budget = JSON.parse(await readFile(budgetPath, "utf8"))[name];
  if (!budget) {
    console.error(`No budget for '${name}' in ${budgetPath}.`);
    process.exit(2);
  }
  table.push("| Budget | Size | Limit | |", "|---|---:|---:|---|");
  for (const key of Object.keys(budget)) {
    const over = sizes[key] > budget[key];
    failed ||= over;
    table.push(`| ${key} | ${sizes[key]} | ${budget[key]} | ${over ? "❌ over budget" : "✅"} |`);
  }
  table.push("");
}

const text = table.join("\n");
console.log(text);
const markdown = option("--markdown");
if (markdown) await appendFile(markdown, text + "\n");

const writeBudget = option("--write-budget");
if (writeBudget) {
  let all = {};
  try { all = JSON.parse(await readFile(writeBudget, "utf8")); } catch { }
  all[name] = Object.fromEntries(Object.entries(sizes).map(([k, v]) => [k, Math.ceil(v * 1.05 / 1024) * 1024]));
  await writeFile(writeBudget, JSON.stringify(all, null, 2) + "\n");
  console.log(`Wrote budget for '${name}' to ${writeBudget}.`);
}

if (failed) {
  console.error(`${name} is over its size budget. If the growth is intended, raise the limit in ${budgetPath} in the same PR.`);
  process.exit(1);
}
