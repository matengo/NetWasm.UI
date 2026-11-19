using System;

namespace NetWasm.UI.Workers;

/// <summary>
/// Marks an interface as a worker contract: the methods a Web Worker offers to the app. Put it in a project that
/// both the app and the worker reference. Methods return <see cref="System.Threading.Tasks.Task"/>,
/// <see cref="System.Threading.Tasks.Task{TResult}"/>, <see cref="System.Threading.Tasks.ValueTask"/>,
/// <see cref="System.Threading.Tasks.ValueTask{TResult}"/> or <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/>,
/// and may take an <see cref="IProgress{T}"/> and a <see cref="System.Threading.CancellationToken"/>.
/// </summary>
/// <remarks>
/// <c>bool</c>, <c>int</c>, <c>float</c>, <c>double</c>, <c>string</c>, <c>byte[]</c>, <c>int[]</c>, <c>float[]</c> and
/// <c>double[]</c> cross natively (arrays are copied once). Other types are serialized with System.Text.Json through
/// <see cref="JsonContext"/>, a <c>JsonSerializerContext</c> that lists them.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class WorkerContractAttribute : Attribute
{
    /// <summary>
    /// The worker project's name (its assembly name), which is where the app finds it:
    /// <c>browser/workers/&lt;Worker&gt;/</c>. <c>AddWorker&lt;T&gt;(worker)</c> can override it.
    /// </summary>
    public string? Worker { get; set; }

    /// <summary>A <c>JsonSerializerContext</c> for the parameter and result types that do not cross natively.</summary>
    public Type? JsonContext { get; set; }
}

/// <summary>
/// Implements one or more <see cref="WorkerContractAttribute"/> interfaces in a worker project
/// (<c>NetWasmJavaScriptHosts=worker</c>). The generator exports its methods to the app. One instance serves all calls
/// to a worker, so fields keep state between calls (an index, a model, a solver session).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WorkerServiceAttribute : Attribute
{
}

/// <summary>A worker call failed: the worker threw, could not start, or was terminated while the call ran.</summary>
public sealed class WorkerException : Exception
{
    /// <summary>Creates the exception.</summary>
    public WorkerException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
