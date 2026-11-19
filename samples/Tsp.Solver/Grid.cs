using System;

namespace Tsp.Solver;

/// <summary>Points bucketed in a uniform grid (about two per cell), for nearest-point searches without n² work.</summary>
internal sealed class Grid
{
    private readonly double[] x;
    private readonly double[] y;
    private readonly double minX;
    private readonly double minY;
    private readonly double cell;
    private readonly int cols;
    private readonly int rows;
    private readonly int[] start;
    private readonly int[] items;
    private readonly int[] cellOf;

    public Grid(double[] x, double[] y)
    {
        this.x = x;
        this.y = y;
        var n = x.Length;
        minX = double.MaxValue;
        minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        for (var i = 0; i < n; i++)
        {
            minX = Math.Min(minX, x[i]);
            minY = Math.Min(minY, y[i]);
            maxX = Math.Max(maxX, x[i]);
            maxY = Math.Max(maxY, y[i]);
        }

        var side = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n / 2.0)));
        cell = Math.Max(Math.Max(maxX - minX, maxY - minY) / side, 1e-6);
        cols = Math.Min(side, (int)((maxX - minX) / cell) + 1);
        rows = Math.Min(side, (int)((maxY - minY) / cell) + 1);

        start = new int[cols * rows + 1];
        cellOf = new int[n];
        for (var i = 0; i < n; i++)
        {
            var cx = Math.Min(cols - 1, (int)((x[i] - minX) / cell));
            var cy = Math.Min(rows - 1, (int)((y[i] - minY) / cell));
            cellOf[i] = cy * cols + cx;
            start[cellOf[i] + 1]++;
        }

        for (var c = 0; c < cols * rows; c++)
        {
            start[c + 1] += start[c];
        }

        items = new int[n];
        var fill = (int[])start.Clone();
        for (var i = 0; i < n; i++)
        {
            items[fill[cellOf[i]]++] = i;
        }
    }

    /// <summary>Fills <paramref name="result"/> with the <paramref name="k"/> nearest other points of each point, nearest first.</summary>
    public void Nearest(int k, int[] result)
    {
        if (k == 0)
        {
            return;
        }

        var dist = new double[k];
        for (var p = 0; p < x.Length; p++)
        {
            var row = p * k;
            var found = 0;
            var cx = cellOf[p] % cols;
            var cy = cellOf[p] / cols;
            for (var r = 0; ; r++)
            {
                if (found == k && dist[k - 1] <= (r - 1) * cell * ((r - 1) * cell))
                {
                    break;
                }

                if (r > cols && r > rows)
                {
                    break;
                }

                for (var gy = cy - r; gy <= cy + r; gy++)
                {
                    if (gy < 0 || gy >= rows)
                    {
                        continue;
                    }

                    var edge = gy == cy - r || gy == cy + r;
                    for (var gx = cx - r; gx <= cx + r; gx += edge ? 1 : 2 * r)
                    {
                        if (gx >= 0 && gx < cols)
                        {
                            var c = gy * cols + gx;
                            for (var t = start[c]; t < start[c + 1]; t++)
                            {
                                var q = items[t];
                                if (q == p)
                                {
                                    continue;
                                }

                                var d = Dist2(p, q);
                                if (found == k && d >= dist[k - 1])
                                {
                                    continue;
                                }

                                var j = found == k ? k - 1 : found++;
                                while (j > 0 && dist[j - 1] > d)
                                {
                                    dist[j] = dist[j - 1];
                                    result[row + j] = result[row + j - 1];
                                    j--;
                                }

                                dist[j] = d;
                                result[row + j] = q;
                            }
                        }

                        if (r == 0)
                        {
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>A greedy tour: from <paramref name="first"/>, always go to the nearest unvisited point.</summary>
    public void NearestNeighbourTour(int first, int[] tour)
    {
        var n = x.Length;
        // The unvisited points of each cell are items[start[c] .. start[c] + left[c]); visiting swaps a point out.
        var left = new int[cols * rows];
        var slot = new int[n];
        for (var c = 0; c < cols * rows; c++)
        {
            left[c] = start[c + 1] - start[c];
            for (var t = start[c]; t < start[c + 1]; t++)
            {
                slot[items[t]] = t;
            }
        }

        var current = first;
        for (var step = 0; ; step++)
        {
            tour[step] = current;
            var c0 = cellOf[current];
            var last = start[c0] + --left[c0];
            var moved = items[last];
            items[slot[current]] = moved;
            slot[moved] = slot[current];
            items[last] = current;
            slot[current] = last;
            if (step == n - 1)
            {
                break;
            }

            var best = -1;
            var bestD = double.MaxValue;
            var cx = c0 % cols;
            var cy = c0 / cols;
            for (var r = 0; ; r++)
            {
                if (best >= 0 && bestD <= (r - 1) * cell * ((r - 1) * cell))
                {
                    break;
                }

                if (r > cols && r > rows)
                {
                    break;
                }

                for (var gy = cy - r; gy <= cy + r; gy++)
                {
                    if (gy < 0 || gy >= rows)
                    {
                        continue;
                    }

                    var edge = gy == cy - r || gy == cy + r;
                    for (var gx = cx - r; gx <= cx + r; gx += edge ? 1 : 2 * r)
                    {
                        if (gx >= 0 && gx < cols)
                        {
                            var c = gy * cols + gx;
                            for (var t = start[c]; t < start[c] + left[c]; t++)
                            {
                                var d = Dist2(current, items[t]);
                                if (d < bestD)
                                {
                                    bestD = d;
                                    best = items[t];
                                }
                            }
                        }

                        if (r == 0)
                        {
                            break;
                        }
                    }
                }
            }

            current = best;
        }

        // Restore the cell lists for later searches.
        var fill = (int[])start.Clone();
        for (var i = 0; i < n; i++)
        {
            items[fill[cellOf[i]]++] = i;
        }
    }

    private double Dist2(int a, int b)
    {
        var dx = x[a] - x[b];
        var dy = y[a] - y[b];
        return dx * dx + dy * dy;
    }
}
