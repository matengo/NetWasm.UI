// Stands in for a third-party library (a chart, map or editor) that owns the DOM inside a container.
let live = 0;

export function createMeter(host, { max = 10, onChange } = {}) {
  const bar = document.createElement("div");
  bar.className = "meter-bar";
  const label = document.createElement("span");
  label.className = "meter-label";
  host.append(bar, label);
  live++;

  const onClick = e => {
    const rect = host.getBoundingClientRect();
    onChange?.(Math.round(((e.clientX - rect.left) / rect.width) * max));
  };
  host.addEventListener("click", onClick);

  return {
    setValue(value) {
      const v = Math.max(0, Math.min(max, value));
      bar.style.width = `${(v / max) * 100}%`;
      label.textContent = `${v} / ${max}`;
    },
    set onChange(fn) { onChange = fn; },
    destroy() {
      host.removeEventListener("click", onClick);
      bar.remove();
      label.remove();
      live--;
    },
  };
}

/** For tests: how many meters have not been destroyed. */
export function liveMeters() { return live; }