using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using NetWasm.UI.Workers;
using Tsp.Contracts;

namespace Tsp.Solver;

/// <summary>Runs <see cref="Tour"/> in a Web Worker and streams each better tour to the page.</summary>
[WorkerService]
public sealed class TspSolverService : ITspSolver
{
    // At most about seven updates a second: drawing 10 000 points is not free on the page either.
    private const int UpdateMs = 150;

    public async IAsyncEnumerable<TourUpdate> SolveAsync(
        double[] points, int seed, int seconds, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var limit = seconds <= 0 ? long.MaxValue : seconds * 1000L;
        var tour = new Tour(points, seed);
        yield return Update(tour, "nearest neighbour", clock, false);

        var phase = "2-opt + Or-opt";
        var sent = clock.ElapsedMilliseconds;
        var shown = tour.Length;
        while (!tour.Improve(256) && clock.ElapsedMilliseconds < limit)
        {
            // Lets a cancel from the page through (about every 8 ms).
            await Worker.YieldAsync(cancellationToken);
            if (clock.ElapsedMilliseconds - sent >= UpdateMs)
            {
                sent = clock.ElapsedMilliseconds;
                shown = tour.Length;
                yield return Update(tour, phase, clock, false);
            }
        }

        yield return Update(tour, phase, clock, false);
        sent = clock.ElapsedMilliseconds;
        shown = tour.Length;

        phase = "perturbation";
        while (tour.Count >= 8 && clock.ElapsedMilliseconds < limit)
        {
            for (var i = 0; i < 16 && clock.ElapsedMilliseconds < limit; i++)
            {
                tour.Perturb();
            }

            await Worker.YieldAsync(cancellationToken);
            if (tour.Length < shown && clock.ElapsedMilliseconds - sent >= UpdateMs)
            {
                sent = clock.ElapsedMilliseconds;
                shown = tour.Length;
                yield return Update(tour, phase, clock, false);
            }
        }

        yield return Update(tour, phase, clock, true);
    }

    private static TourUpdate Update(Tour tour, string phase, Stopwatch clock, bool done) => new()
    {
        Order = tour.Order(),
        Length = tour.Length,
        Phase = phase,
        Moves = tour.Moves,
        Kicks = tour.Kicks,
        ElapsedMs = (int)clock.ElapsedMilliseconds,
        Done = done,
    };
}
