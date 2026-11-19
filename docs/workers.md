# Web Workers

`NwUi.Workers` (preview) runs C# in Web Workers, so heavy computation (search indexes, solvers,
parsing, image work) does not freeze the page. You write an interface, implement it in a worker
project and inject it in the app. A source generator writes the message passing on both sides.

```csharp
// Shared: Contracts project
[WorkerContract(Worker = "MyApp.Worker")]
public interface ISolver
{
    Task<int> SolveAsync(int size, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
}

// Worker project (MyApp.Worker)
[WorkerService]
public sealed class Solver : ISolver
{
    public async Task<int> SolveAsync(int size, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        for (var i = 0; i < size; i++)
        {
            // ... work ...
            await Worker.YieldAsync(cancellationToken); // lets a cancel through
        }

        return size;
    }
}

// App
new Services().AddWorker<ISolver>();

// A component
@inject ISolver Solver
var answer = await Solver.SolveAsync(1000, Worker.Progress<int>(p => percent.Value = p), ct);
```

The sample is [`samples/Workers`](../samples/Workers), with its worker in
[`samples/Workers.Primes`](../samples/Workers.Primes) and the interfaces in
[`samples/Workers.Contracts`](../samples/Workers.Contracts).

## Projects

Three projects, all `netwasm0.1`, each with `dotnet add package NwUi.Workers`. The package brings the
source generator and the JavaScript modules.

| Project | Contains | References |
|---|---|---|
| Contracts (`Library`) | `[WorkerContract]` interfaces and their JSON types | `NwUi.Workers` |
| Worker (`JsModule`, `<NetWasmJavaScriptHosts>worker</NetWasmJavaScriptHosts>`) | `[WorkerService]` classes | Contracts, `NwUi.Workers` |
| App | Components; `AddWorker<T>()` | Contracts, `NwUi.Workers`, the worker with `NetWasmWorker="true"` |

```xml
<!-- App .csproj: publishes the worker into browser/workers/MyApp.Worker/ -->
<ProjectReference Include="..\MyApp.Worker\MyApp.Worker.csproj" NetWasmWorker="true" />
```

The worker name in `[WorkerContract(Worker = "...")]` is the worker project's assembly name. You can
also pass it when registering: `AddWorker<ISolver>(worker: "MyApp.Worker")`. One worker project can
implement several contracts.

## One worker or a pool

| | `AddWorker<T>()` | `AddWorkerPool<T>(size)` |
|---|---|---|
| Workers | One, started on the first call | Up to `size` (default `Worker.DefaultPoolSize`: `navigator.hardwareConcurrency - 1`), started on demand |
| State | Kept between calls (an index, a loaded model) | Do not rely on it: each call goes to any idle worker |
| Calls | Run concurrently in the one worker | One per worker at a time; the rest wait in order |
| Use for | Stateful services | Splitting work in parallel with `Task.WhenAll` |

```csharp
new Services()
    .AddWorker<IPrimeWorker>()            // one worker that keeps its state
    .AddWorkerPool<IPrimeMath>(size: 4);  // four workers for parallel, stateless calls

var counts = await Task.WhenAll(parts.Select(p => math.CountRangeAsync(p.From, p.To)));
```

The service is disposed with its scope, and that terminates its workers.

## Contracts

A contract is an interface marked `[WorkerContract]`. Its methods return `Task`, `Task<T>`,
`ValueTask`, `ValueTask<T>` or `IAsyncEnumerable<T>`, and can take:

- `bool`, `int`, `float`, `double`, `string`, and `byte[]`, `int[]`, `float[]`, `double[]`, passed
  directly;
- any other type as JSON, with a `JsonSerializerContext` named in
  `[WorkerContract(JsonContext = typeof(MyJson))]` that lists it with `[JsonSerializable]`;
- one `CancellationToken`;
- one `IProgress<T>`.

Overloads, generic methods, `ref`, `out` and `params` are not supported. The generator reports
`NWUI101` for an invalid contract, `NWUI102` when no worker name is given and `NWUI103` for a
`[WorkerService]` class that implements no contract.

## Cancellation

Cancelling the token cancels the token the worker method received. The worker sees it the next time
it calls `await Worker.YieldAsync(cancellationToken)`, which also lets the worker's event loop run
about every 8 ms (otherwise the cancel message cannot arrive). `await Task.Yield()` does not do
this. The worker keeps running and keeps its state.

For code that cannot check a token, register with `terminateOnCancel: true`: a cancel terminates the
worker. Its state is lost and the next call starts a new one.

## Progress and streams

Pass `Worker.Progress<T>(Action<T>)` for `IProgress<T>`: it calls your action as each report
arrives. (`System.Progress<T>` posts through a `SynchronizationContext` instead.) Reports set
signals directly:

```csharp
var report = Worker.Progress<int>(p => progress.Value = p);
```

An `IAsyncEnumerable<T>` method streams items as the worker yields them, with back pressure: the
worker pauses when the app has not read the items it already sent. Leaving an `await foreach`
early cancels the worker's enumeration.

```csharp
await foreach (var p in primes.PrimesAsync(1000))
{
    if (++n == 10) break;
}
```

## Errors

An exception in the worker reaches the app as a `WorkerException` with the worker's message.
An `OperationCanceledException` stays an `OperationCanceledException`.

## Deployment

Each worker is its own Wasm module in `browser/workers/<name>/`, loaded the first time it is
called. The Content Security Policy in [Security](security.md) already allows it: without
`worker-src`, workers fall back to `script-src`.
