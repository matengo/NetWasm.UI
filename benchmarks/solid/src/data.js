// Same labels and LCG as benchmarks/NetWasmBench/Data.cs, so both apps render identical rows.
const adjectives = [
  "pretty", "large", "big", "small", "tall", "short", "long", "handsome", "plain", "quaint", "clean",
  "elegant", "easy", "angry", "crazy", "helpful", "mushy", "odd", "unsightly", "adorable", "important",
  "inexpensive", "cheap", "expensive", "fancy",
];
const colours = ["red", "yellow", "blue", "green", "pink", "brown", "purple", "brown", "white", "black", "orange"];
const nouns = ["table", "chair", "house", "bbq", "desk", "car", "pony", "cookie", "sandwich", "burger", "pizza", "mouse", "keyboard"];

export function random(state, max) {
  state.seed = (Math.imul(state.seed, 1103515245) + 12345) & 0x7fffffff;
  return state.seed % max;
}

export function label(state) {
  return adjectives[random(state, adjectives.length)] + " " + colours[random(state, colours.length)] + " " +
    nouns[random(state, nouns.length)];
}

const appState = { seed: 42 };
let nextId = 1;

export function buildRows(count, createLabel) {
  const rows = new Array(count);
  for (let i = 0; i < count; i++) {
    rows[i] = createLabel(nextId++, label(appState));
  }
  return rows;
}
