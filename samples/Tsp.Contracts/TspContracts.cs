using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using NetWasm.UI.Workers;

namespace Tsp.Contracts;

/// <summary>A route optimizer (traveling salesman) that runs in a Web Worker.</summary>
[WorkerContract(Worker = "Tsp.Solver", JsonContext = typeof(TspJson))]
public interface ITspSolver
{
    /// <summary>
    /// Finds a short round trip through <paramref name="points"/> (x0, y0, x1, y1, …) and streams each better tour as it
    /// is found. Runs for <paramref name="seconds"/>, or until cancelled when it is 0; the last item has
    /// <see cref="TourUpdate.Done"/> set (sooner for fewer than 8 points, where there is nothing more to try). <paramref name="seed"/> picks the start city and the random kicks, so
    /// workers with different seeds search differently.
    /// </summary>
    IAsyncEnumerable<TourUpdate> SolveAsync(double[] points, int seed, int seconds, CancellationToken cancellationToken = default);
}

/// <summary>A tour found by the solver.</summary>
public sealed class TourUpdate
{
    /// <summary>The cities in visiting order (indexes into the points).</summary>
    public int[] Order { get; set; } = Array.Empty<int>();

    /// <summary>The length of the round trip.</summary>
    public double Length { get; set; }

    /// <summary>What the solver is doing: "nearest neighbour", "2-opt + Or-opt" or "perturbation".</summary>
    public string Phase { get; set; } = "";

    /// <summary>Improving moves made so far.</summary>
    public long Moves { get; set; }

    /// <summary>Random kicks tried in the perturbation phase.</summary>
    public long Kicks { get; set; }

    /// <summary>Milliseconds since the solver started.</summary>
    public int ElapsedMs { get; set; }

    /// <summary>True for the last update, when the time is up.</summary>
    public bool Done { get; set; }
}

[JsonSerializable(typeof(TourUpdate))]
public sealed partial class TspJson : JsonSerializerContext
{
}
