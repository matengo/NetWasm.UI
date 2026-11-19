using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NetWasm.UI.Workers;

namespace Workers.Contracts;

/// <summary>Prime work for one dedicated worker, which keeps its state between calls.</summary>
[WorkerContract(Worker = "Workers.Primes", JsonContext = typeof(PrimeJson))]
public interface IPrimeWorker
{
    /// <summary>Counts the primes up to <paramref name="limit"/>, reporting each percent.</summary>
    Task<int> CountAsync(int limit, IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Statistics for a range: a JSON argument and result.</summary>
    Task<PrimeStats> StatsAsync(PrimeRange range, CancellationToken cancellationToken = default);

    /// <summary>The primes from <paramref name="from"/> on, without end: the caller stops reading.</summary>
    IAsyncEnumerable<int> PrimesAsync(int from, CancellationToken cancellationToken = default);

    /// <summary>How many counts this worker has started: its state outlives each call.</summary>
    Task<int> RunsAsync();
}

/// <summary>Stateless prime work, for a pool of workers.</summary>
[WorkerContract(Worker = "Workers.Primes")]
public interface IPrimeMath
{
    /// <summary>Counts the primes in [<paramref name="from"/>, <paramref name="to"/>).</summary>
    Task<int> CountRangeAsync(int from, int to, CancellationToken cancellationToken = default);
}

/// <summary>A range of numbers.</summary>
public sealed class PrimeRange
{
    public int From { get; set; }

    public int To { get; set; }
}

/// <summary>Primes found in a range.</summary>
public sealed class PrimeStats
{
    public int Count { get; set; }

    public int Largest { get; set; }

    public int TwinPairs { get; set; }
}

[JsonSerializable(typeof(PrimeRange))]
[JsonSerializable(typeof(PrimeStats))]
public sealed partial class PrimeJson : JsonSerializerContext
{
}
