using System;

namespace NetWasm.UI.Workers;

/// <summary>Registers worker contracts as services; components inject the contract and call the worker through it.</summary>
public static class WorkerServices
{
    /// <summary>
    /// Registers <typeparamref name="T"/>, a <see cref="WorkerContractAttribute"/> interface, as a service backed by one
    /// dedicated worker. The worker starts on the first call and keeps its state between calls; calls run concurrently
    /// in it. Cancelling a call cancels the token the worker method received (cooperative), or terminates the worker
    /// when <paramref name="terminateOnCancel"/> is set (its state is lost; the next call starts a new one). The NwUi.Workers
    /// source generator rewrites the call at compile time.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="worker">The worker project's name; defaults to <see cref="WorkerContractAttribute.Worker"/>.</param>
    /// <param name="terminateOnCancel">Terminate the worker when a call is cancelled, instead of cancelling its token.</param>
    /// <exception cref="InvalidOperationException">The call was not rewritten (the generator is not running for this project).</exception>
    public static Services AddWorker<T>(this Services services, string? worker = null, bool terminateOnCancel = false)
        where T : class => throw NotGenerated();

    /// <summary>
    /// Registers <typeparamref name="T"/> as a service backed by a pool of up to <paramref name="size"/> workers
    /// (<see cref="Worker.DefaultPoolSize"/> when 0). Each call runs alone in an idle worker, started on demand; when all are
    /// busy, calls wait in order. Use it for stateless work in parallel: <c>await Task.WhenAll(parts.Select(p =&gt; solver.Solve(p)))</c>.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="size">The most workers to run at once; 0 for <see cref="Worker.DefaultPoolSize"/>.</param>
    /// <param name="worker">The worker project's name; defaults to <see cref="WorkerContractAttribute.Worker"/>.</param>
    /// <param name="terminateOnCancel">Terminate (and later replace) a worker whose call is cancelled, instead of cancelling its token.</param>
    /// <exception cref="InvalidOperationException">The call was not rewritten (the generator is not running for this project).</exception>
    public static Services AddWorkerPool<T>(this Services services, int size = 0, string? worker = null, bool terminateOnCancel = false)
        where T : class => throw NotGenerated();

    private static InvalidOperationException NotGenerated() => new(
        "AddWorker<T>() and AddWorkerPool<T>() are rewritten at compile time by the NwUi.Workers source generator, which did not run for this call. " +
        "Reference the NwUi.Workers package (or its generator as an analyzer) and include NetWasm.UI.Generated in InterceptorsNamespaces.");
}
