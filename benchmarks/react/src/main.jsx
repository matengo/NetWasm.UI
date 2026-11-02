import { memo, useLayoutEffect, useReducer } from "react";
import { createRoot } from "react-dom/client";
import { buildRows } from "../../solid/src/data.js";

// The js-framework-benchmark keyed react-hooks implementation, adapted to the shared data generator.
const build = count => buildRows(count, (id, label) => ({ id, label }));

function listReducer(state, action) {
  const { data, selected } = state;
  switch (action.type) {
    case "RUN":
      return { data: build(1000), selected: 0 };
    case "RUN_LOTS":
      return { data: build(10000), selected: 0 };
    case "ADD":
      return { data: data.concat(build(1000)), selected };
    case "UPDATE": {
      const newData = data.slice(0);
      for (let i = 0; i < newData.length; i += 10) {
        const r = newData[i];
        newData[i] = { id: r.id, label: r.label + " !!!" };
      }
      return { data: newData, selected };
    }
    case "CLEAR":
      return { data: [], selected: 0 };
    case "SWAP_ROWS": {
      const newData = [...data];
      if (data.length > 998) {
        const d1 = newData[1];
        newData[1] = newData[998];
        newData[998] = d1;
      }
      return { data: newData, selected };
    }
    case "REMOVE": {
      const idx = data.findIndex(d => d.id === action.id);
      return { data: [...data.slice(0, idx), ...data.slice(idx + 1)], selected };
    }
    case "SELECT":
      return { data, selected: action.id };
    default:
      return state;
  }
}

const Row = memo(
  ({ selected, item, dispatch }) => (
    <tr className={selected ? "danger" : ""}>
      <td className="col-md-1">{item.id}</td>
      <td className="col-md-4"><a onClick={() => dispatch({ type: "SELECT", id: item.id })}>{item.label}</a></td>
      <td className="col-md-1">
        <a onClick={() => dispatch({ type: "REMOVE", id: item.id })}>
          <span className="glyphicon glyphicon-remove" aria-hidden="true" />
        </a>
      </td>
      <td className="col-md-6" />
    </tr>
  ),
  (prev, next) => prev.selected === next.selected && prev.item === next.item,
);

const Button = ({ id, cb, title }) => (
  <button id={id} type="button" onClick={cb}>{title}</button>
);

const Main = () => {
  const [{ data, selected }, dispatch] = useReducer(listReducer, { data: [], selected: 0 });
  useLayoutEffect(() => {
    window.__ready = performance.now();
  }, []);

  return (
    <div className="container">
      <div className="jumbotron">
        <h1>React keyed</h1>
        <div className="buttons">
          <Button id="run" title="Create 1,000 rows" cb={() => dispatch({ type: "RUN" })} />
          <Button id="runlots" title="Create 10,000 rows" cb={() => dispatch({ type: "RUN_LOTS" })} />
          <Button id="add" title="Append 1,000 rows" cb={() => dispatch({ type: "ADD" })} />
          <Button id="update" title="Update every 10th row" cb={() => dispatch({ type: "UPDATE" })} />
          <Button id="clear" title="Clear" cb={() => dispatch({ type: "CLEAR" })} />
          <Button id="swaprows" title="Swap Rows" cb={() => dispatch({ type: "SWAP_ROWS" })} />
        </div>
      </div>
      <table className="table table-hover table-striped test-data">
        <tbody id="tbody">
          {data.map(item => (
            <Row key={item.id} item={item} selected={selected === item.id} dispatch={dispatch} />
          ))}
        </tbody>
      </table>
      <span className="preloadicon glyphicon glyphicon-remove" aria-hidden="true" />
    </div>
  );
};

// React has no signals, so the reactive workloads do not apply; sort and sieve would just repeat Solid's JS.
window.bench = { reactive: () => null };
createRoot(document.getElementById("main")).render(<Main />);
