using System;

namespace Tsp.Solver;

/// <summary>
/// A round trip through points in the plane, improved in small steps so a worker can yield in between:
/// nearest neighbour (grid search) for a first tour, then 2-opt and Or-opt moves on k-nearest candidate lists with
/// a work queue ("don't-look bits"), then iterated local search: a random local double-bridge kick, a repair with
/// the same moves, and an undo when the result is not shorter. Distances are computed on the fly, so memory grows
/// with n, not n².
/// </summary>
public sealed class Tour
{
    private const double Epsilon = 1e-9;

    private readonly int n;
    private readonly double[] x;
    private readonly double[] y;
    private readonly int[] neighbors;
    private readonly int k;
    private readonly int[] tour;
    private readonly int[] pos;
    private readonly int[] bestTour;
    private readonly int[] bestPos;
    private readonly int[] queue;
    private readonly bool[] queued;
    private int head;
    private int count;
    private double bestLength;
    private uint rng;

    /// <param name="points">x0, y0, x1, y1, …</param>
    /// <param name="seed">Picks the start city and the kicks.</param>
    public Tour(double[] points, int seed)
    {
        n = points.Length / 2;
        x = new double[n];
        y = new double[n];
        for (var i = 0; i < n; i++)
        {
            x[i] = points[2 * i];
            y[i] = points[2 * i + 1];
        }

        rng = (uint)seed * 2654435761u + 0x9E3779B9u;
        if (rng == 0)
        {
            rng = 1;
        }

        k = Math.Min(10, Math.Max(0, n - 1));
        neighbors = new int[n * k];
        tour = new int[n];
        pos = new int[n];
        bestTour = new int[n];
        bestPos = new int[n];
        queue = new int[n];
        queued = new bool[n];
        if (n > 0)
        {
            var grid = new Grid(x, y);
            grid.Nearest(k, neighbors);
            grid.NearestNeighbourTour(n <= 1 ? 0 : (int)(Next() % (uint)n), tour);
        }

        for (var i = 0; i < n; i++)
        {
            pos[tour[i]] = i;
        }

        Length = ComputeLength();
        Save();
        for (var i = 0; i < n; i++)
        {
            Push(tour[i]);
        }
    }

    /// <summary>The number of cities.</summary>
    public int Count => n;

    /// <summary>The length of the current tour.</summary>
    public double Length { get; private set; }

    /// <summary>Improving moves made so far.</summary>
    public long Moves { get; private set; }

    /// <summary>Kicks tried so far.</summary>
    public long Kicks { get; private set; }

    /// <summary>True when no city is left to check: the tour is 2-opt and Or-opt optimal for the candidate lists.</summary>
    public bool LocalOptimum => count == 0;

    /// <summary>The cities in visiting order (a copy).</summary>
    public int[] Order() => (int[])tour.Clone();

    /// <summary>The tour length computed from scratch (to check the running <see cref="Length"/>).</summary>
    public double ComputeLength()
    {
        var length = 0.0;
        for (var i = 0; i < n; i++)
        {
            length += Dist(tour[i], tour[i + 1 == n ? 0 : i + 1]);
        }

        return length;
    }

    /// <summary>Checks up to <paramref name="budget"/> queued cities for improving moves.</summary>
    /// <returns>True when the tour is at a local optimum.</returns>
    public bool Improve(int budget)
    {
        if (n < 5)
        {
            count = 0;
            return true;
        }

        while (count > 0 && budget-- > 0)
        {
            var a = Pop();
            if (TwoOpt(a) || OrOpt(a))
            {
                Push(a);
            }
        }

        return count == 0;
    }

    /// <summary>
    /// One step of iterated local search, from a local optimum: kick, repair, keep it when shorter, else undo.
    /// </summary>
    /// <returns>True when the tour got shorter.</returns>
    public bool Perturb()
    {
        if (n < 8)
        {
            return false;
        }

        Kicks++;
        if (Length != bestLength)
        {
            Save();
        }

        var before = Length;
        Kick();
        Improve(int.MaxValue);
        if (Length < before - Epsilon)
        {
            Save();
            return true;
        }

        Array.Copy(bestTour, tour, n);
        Array.Copy(bestPos, pos, n);
        Length = before;
        return false;
    }

    private void Save()
    {
        bestLength = Length;
        Array.Copy(tour, bestTour, n);
        Array.Copy(pos, bestPos, n);
    }

    // Double bridge on three short consecutive segments: A B C D -> A C B D, a move 2-opt and Or-opt cannot undo.
    private void Kick()
    {
        var max = Math.Min(50, (n - 2) / 3);
        var l1 = 1 + (int)(Next() % (uint)max);
        var l2 = 1 + (int)(Next() % (uint)max);
        var start = (int)(Next() % (uint)n);
        var a = tour[start];
        var b1 = tour[Wrap(start + 1)];
        var b2 = tour[Wrap(start + l1)];
        var c1 = tour[Wrap(start + l1 + 1)];
        var c2 = tour[Wrap(start + l1 + l2)];
        var d = tour[Wrap(start + l1 + l2 + 1)];
        Length += Dist(a, c1) + Dist(c2, b1) + Dist(b2, d) - Dist(a, b1) - Dist(b2, c1) - Dist(c2, d);

        var segment = new int[l1 + l2];
        for (var i = 0; i < l2; i++)
        {
            segment[i] = tour[Wrap(start + l1 + 1 + i)];
        }

        for (var i = 0; i < l1; i++)
        {
            segment[l2 + i] = tour[Wrap(start + 1 + i)];
        }

        for (var i = 0; i < segment.Length; i++)
        {
            var p = Wrap(start + 1 + i);
            tour[p] = segment[i];
            pos[segment[i]] = p;
        }

        Push(a);
        Push(b1);
        Push(b2);
        Push(c1);
        Push(c2);
        Push(d);
    }

    // Replaces edges (a, succ a) and (c, succ c), or (pred a, a) and (pred c, c), with (a, c) and the other two ends.
    private bool TwoOpt(int a)
    {
        for (var dir = 0; dir < 2; dir++)
        {
            var b = dir == 0 ? Succ(a) : Pred(a);
            var ab = Dist(a, b);
            var row = a * k;
            for (var i = 0; i < k; i++)
            {
                var c = neighbors[row + i];
                var g1 = ab - Dist(a, c);
                if (g1 <= Epsilon)
                {
                    break;
                }

                var d = dir == 0 ? Succ(c) : Pred(c);
                if (c == b || d == a)
                {
                    continue;
                }

                var gain = g1 + Dist(c, d) - Dist(b, d);
                if (gain > Epsilon)
                {
                    Make2Opt(a, b, c, d);
                    Length -= gain;
                    Moves++;
                    Push(b);
                    Push(c);
                    Push(d);
                    return true;
                }
            }
        }

        return false;
    }

    // Moves the segment of 1-3 cities that starts at s1 next to one of its ends' neighbours, either way round.
    private bool OrOpt(int s1)
    {
        for (var length = 1; length <= 3 && length + 3 <= n; length++)
        {
            var s2 = tour[Wrap(pos[s1] + length - 1)];
            var p = Pred(s1);
            var nx = Succ(s2);
            var removed = Dist(p, s1) + Dist(s2, nx) - Dist(p, nx);
            if (removed <= Epsilon)
            {
                continue;
            }

            for (var end = 0; end < 2; end++)
            {
                var from = end == 0 ? s1 : s2;
                var row = from * k;
                for (var i = 0; i < k; i++)
                {
                    var c = neighbors[row + i];
                    if (Dist(from, c) >= removed)
                    {
                        break;
                    }

                    if (InSegment(c, s1, length))
                    {
                        continue;
                    }

                    for (var side = 0; side < 2; side++)
                    {
                        var e1 = side == 0 ? c : Pred(c);
                        var e2 = side == 0 ? Succ(c) : c;
                        if (InSegment(e1, s1, length) || InSegment(e2, s1, length) || e2 == p)
                        {
                            continue;
                        }

                        var edge = Dist(e1, e2);
                        var reversed = Dist(e1, s2) + Dist(s1, e2) - edge;
                        var forward = length == 1 ? double.MaxValue : Dist(e1, s1) + Dist(s2, e2) - edge;
                        var keep = forward < reversed;
                        var gain = removed - (keep ? forward : reversed);
                        if (gain > Epsilon)
                        {
                            // Three 2-opt moves: p s1..s2 nx … e1 e2  ->  p nx … e1 s2..s1 e2  (-> e1 s1..s2 e2).
                            Make2Opt(p, s1, e1, e2);
                            Make2Opt(p, e1, nx, s2);
                            if (keep)
                            {
                                Make2Opt(e1, s2, s1, e2);
                            }

                            Length -= gain;
                            Moves++;
                            Push(p);
                            Push(nx);
                            Push(e1);
                            Push(e2);
                            Push(s2);
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private bool InSegment(int c, int s1, int length) => Wrap(pos[c] - pos[s1]) < length;

    // Edges {p, q} and {r, s} with the same orientation become {p, r} and {q, s}.
    private void Make2Opt(int p, int q, int r, int s)
    {
        if (Succ(p) == q)
        {
            Reverse(pos[q], pos[r]);
        }
        else
        {
            Reverse(pos[p], pos[s]);
        }
    }

    // Reverses the path from position i to j (going forward), or the rest of the cycle when that is shorter.
    private void Reverse(int i, int j)
    {
        var length = Wrap(j - i) + 1;
        if (length * 2 > n)
        {
            var t = Wrap(j + 1);
            j = Wrap(i - 1);
            i = t;
            length = n - length;
        }

        for (var m = 0; m < length / 2; m++)
        {
            var ci = tour[i];
            var cj = tour[j];
            tour[i] = cj;
            pos[cj] = i;
            tour[j] = ci;
            pos[ci] = j;
            i = i + 1 == n ? 0 : i + 1;
            j = j == 0 ? n - 1 : j - 1;
        }
    }

    private int Succ(int c)
    {
        var p = pos[c] + 1;
        return tour[p == n ? 0 : p];
    }

    private int Pred(int c)
    {
        var p = pos[c];
        return tour[p == 0 ? n - 1 : p - 1];
    }

    private int Wrap(int i)
    {
        i %= n;
        return i < 0 ? i + n : i;
    }

    private double Dist(int a, int b)
    {
        var dx = x[a] - x[b];
        var dy = y[a] - y[b];
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void Push(int c)
    {
        if (queued[c])
        {
            return;
        }

        queued[c] = true;
        queue[(head + count) % n] = c;
        count++;
    }

    private int Pop()
    {
        var c = queue[head];
        head = head + 1 == n ? 0 : head + 1;
        count--;
        queued[c] = false;
        return c;
    }

    private uint Next()
    {
        // xorshift32: deterministic for a seed, and cheap.
        rng ^= rng << 13;
        rng ^= rng >> 17;
        rng ^= rng << 5;
        return rng;
    }
}
