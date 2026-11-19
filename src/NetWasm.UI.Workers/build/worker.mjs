// NetWasm.UI typed workers, worker side ("nwui.workers.worker"): notifications to the page and yielding to
// the worker's event loop, so a call for __nwui_cancel can run while a long computation awaits Worker.YieldAsync.
export function createWorkerImports(notify) {
  const send = (callId, channel, v) => notify("nwui", [callId, channel, v]);
  const channel = new MessageChannel();
  const waiting = [];
  channel.port1.onmessage = () => waiting.shift()?.();
  return Object.freeze({
    notifyBool: send, notifyI32: send, notifyI64: send, notifyF32: send, notifyF64: send, notifyStr: send,
    notifyBytes: send, notifyI32Array: send, notifyF32Array: send, notifyF64Array: send,
    yieldToHost() {
      return new Promise(resolve => {
        waiting.push(resolve);
        channel.port2.postMessage(0);
      });
    },
  });
}
