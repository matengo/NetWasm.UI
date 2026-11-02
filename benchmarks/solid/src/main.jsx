import { render } from "solid-js/web";
import { batch, createSelector, createSignal, For } from "solid-js";
import { buildRows } from "./data.js";
import { reactive } from "./reactive.js";

// The js-framework-benchmark keyed Solid implementation, adapted to the shared data generator.
const createRow = (id, text) => {
  const [label, setLabel] = createSignal(text);
  return { id, label, setLabel };
};
const build = count => buildRows(count, createRow);

const Button = ({ id, text, fn }) => (
  <button id={id} type="button" onClick={fn}>{text}</button>
);

const App = () => {
  const [data, setData] = createSignal([]);
  const [selected, setSelected] = createSignal(null);
  const isSelected = createSelector(selected);

  const run = () => setData(build(1000));
  const runLots = () => setData(build(10000));
  const add = () => setData(d => [...d, ...build(1000)]);
  const update = () => batch(() => {
    const d = data();
    for (let i = 0; i < d.length; i += 10) {
      d[i].setLabel(l => l + " !!!");
    }
  });
  const clear = () => setData([]);
  const swapRows = () => {
    const d = data().slice();
    if (d.length > 998) {
      const tmp = d[1];
      d[1] = d[998];
      d[998] = tmp;
      setData(d);
    }
  };
  const remove = id => setData(d => {
    const idx = d.findIndex(r => r.id === id);
    return [...d.slice(0, idx), ...d.slice(idx + 1)];
  });

  return (
    <div class="container">
      <div class="jumbotron">
        <h1>SolidJS keyed</h1>
        <div class="buttons">
          <Button id="run" text="Create 1,000 rows" fn={run} />
          <Button id="runlots" text="Create 10,000 rows" fn={runLots} />
          <Button id="add" text="Append 1,000 rows" fn={add} />
          <Button id="update" text="Update every 10th row" fn={update} />
          <Button id="clear" text="Clear" fn={clear} />
          <Button id="swaprows" text="Swap Rows" fn={swapRows} />
        </div>
      </div>
      <table class="table table-hover table-striped test-data">
        <tbody id="tbody">
          <For each={data()}>
            {row => {
              const rowId = row.id;
              return (
                <tr class={isSelected(rowId) ? "danger" : ""}>
                  <td class="col-md-1" textContent={rowId} />
                  <td class="col-md-4"><a onClick={() => setSelected(rowId)} textContent={row.label()} /></td>
                  <td class="col-md-1"><a onClick={() => remove(rowId)}><span class="glyphicon glyphicon-remove" aria-hidden="true" /></a></td>
                  <td class="col-md-6" />
                </tr>
              );
            }}
          </For>
        </tbody>
      </table>
      <span class="preloadicon glyphicon glyphicon-remove" aria-hidden="true" />
    </div>
  );
};

render(App, document.getElementById("main"));
window.bench = { reactive };
window.__ready = performance.now();
