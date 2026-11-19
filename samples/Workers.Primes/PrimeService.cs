using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetWasm.UI.Workers;
using Workers.Contracts;

namespace Workers.Primes;

/// <summary>CPU-bound work that would freeze the page if it ran there.</summary>
[WorkerService]
public sealed class PrimeService : IPrimeWorker, IPrimeMath
{
    private int runs;

    public async Task<int> CountAsync(int limit, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        runs++;
        var count = 0;
        var reported = 0;
        for (var n = 2; n <= limit; n++)
        {
            if (IsPrime(n))
            {
                count++;
            }

            if ((n & 4095) == 0)
            {
                // Lets a cancel from the page through (about every 8 ms).
                await Worker.YieldAsync(cancellationToken);
            }

            var percent = (int)(n * 100L / limit);
            if (percent != reported)
            {
                reported = percent;
                progress?.Report(percent);
            }
        }

        return count;
    }

    public Task<PrimeStats> StatsAsync(PrimeRange range, CancellationToken cancellationToken)
    {
        var stats = new PrimeStats();
        var previous = -10;
        for (var n = Math.Max(2, range.From); n < range.To; n++)
        {
            if (IsPrime(n))
            {
                stats.Count++;
                stats.Largest = n;
                if (n - previous == 2)
                {
                    stats.TwinPairs++;
                }

                previous = n;
            }
        }

        return Task.FromResult(stats);
    }

    public async IAsyncEnumerable<int> PrimesAsync(int from, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var n = Math.Max(2, from); n > 0; n++)
        {
            if (IsPrime(n))
            {
                yield return n;
            }

            await Worker.YieldAsync(cancellationToken);
        }
    }

    public Task<int> RunsAsync() => Task.FromResult(runs);

    public async Task<int> CountRangeAsync(int from, int to, CancellationToken cancellationToken)
    {
        var count = 0;
        for (var n = Math.Max(2, from); n < to; n++)
        {
            if (IsPrime(n))
            {
                count++;
            }

            if ((n & 4095) == 0)
            {
                await Worker.YieldAsync(cancellationToken);
            }
        }

        return count;
    }

    private static bool IsPrime(int n)
    {
        if (n < 4)
        {
            return n > 1;
        }

        if (n % 2 == 0)
        {
            return false;
        }

        for (var d = 3; d <= n / d; d += 2)
        {
            if (n % d == 0)
            {
                return false;
            }
        }

        return true;
    }
}
