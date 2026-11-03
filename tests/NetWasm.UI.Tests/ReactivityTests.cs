using NetWasm.UI.Reactive;

namespace NetWasm.UI.Tests;

public class ReactivityTests : IDisposable
{
    public ReactivityTests() => Scheduler.Reset();
    public void Dispose() => Scheduler.Reset();

    [Fact]
    public void Effect_runs_immediately_and_on_change()
    {
        var count = new Signal<int>(0);
        var seen = new List<int>();
        _ = new Effect(() => seen.Add(count.Value));

        count.Value = 1;
        count.Value = 2;

        Assert.Equal(new[] { 0, 1, 2 }, seen);
    }

    [Fact]
    public void Many_observers_and_sources_stay_linked_when_some_are_disposed()
    {
        // More than the inline first link plus the initial overflow array, on both sides.
        var signals = new Signal<int>[12];
        for (var i = 0; i < signals.Length; i++)
        {
            signals[i] = new Signal<int>(0);
        }

        var runs = new int[12];
        var effects = new Effect[12];
        for (var e = 0; e < effects.Length; e++)
        {
            var index = e;
            effects[e] = new Effect(() =>
            {
                for (var i = 0; i < signals.Length; i++)
                {
                    _ = signals[i].Value;
                }

                runs[index]++;
            });
        }

        // Dispose every third effect: swap-removes from the middle of each signal's observer list.
        for (var e = 0; e < effects.Length; e += 3)
        {
            effects[e].Dispose();
        }

        signals[0].Value = 1;
        signals[11].Value = 1;

        for (var e = 0; e < effects.Length; e++)
        {
            Assert.Equal(e % 3 == 0 ? 1 : 3, runs[e]);
        }

        // Re-running re-links everything; the survivors still see each write exactly once.
        Scheduler.Batch(() =>
        {
            for (var i = 0; i < signals.Length; i++)
            {
                signals[i].Value = 2;
            }
        });

        for (var e = 0; e < effects.Length; e++)
        {
            Assert.Equal(e % 3 == 0 ? 1 : 4, runs[e]);
        }
    }

    [Fact]
    public void Equal_writes_do_not_rerun()
    {
        var count = new Signal<int>(5);
        var runs = 0;
        _ = new Effect(() => { _ = count.Value; runs++; });

        count.Value = 5;

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Batch_runs_effect_once_for_many_writes()
    {
        var a = new Signal<int>(0);
        var b = new Signal<int>(0);
        var runs = 0;
        _ = new Effect(() => { _ = a.Value + b.Value; runs++; });

        Scheduler.Batch(() =>
        {
            a.Value = 1;
            b.Value = 2;
            a.Value = 3;
        });

        Assert.Equal(2, runs);
    }

    [Fact]
    public void Diamond_recomputes_its_join_once_per_change()
    {
        var a = new Signal<int>(1);
        var b = new Computed<int>(() => a.Value + 1);
        var c = new Computed<int>(() => a.Value * 2);
        var joins = 0;
        var d = new Computed<int>(() => { joins++; return b.Value + c.Value; });
        var seen = new List<int>();
        _ = new Effect(() => seen.Add(d.Value));

        a.Value = 2;
        a.Value = 3;

        Assert.Equal(new[] { 4, 7, 10 }, seen);
        Assert.Equal(3, joins);
    }

    [Fact]
    public void Unchanged_computed_stops_propagation_through_a_chain()
    {
        var a = new Signal<int>(1);
        var parity = new Computed<bool>(() => a.Value % 2 == 0);
        var downstream = 0;
        var label = new Computed<string>(() => { downstream++; return parity.Value ? "even" : "odd"; });
        var runs = 0;
        _ = new Effect(() => { runs++; _ = label.Value; });

        a.Value = 3;
        a.Value = 5;
        Assert.Equal(1, downstream);
        Assert.Equal(1, runs);

        a.Value = 6;
        Assert.Equal(2, downstream);
        Assert.Equal(2, runs);
        Assert.Equal("even", label.Value);
    }

    [Fact]
    public void Unread_computed_is_not_recomputed_on_change()
    {
        var a = new Signal<int>(1);
        var computes = 0;
        var doubled = new Computed<int>(() => { computes++; return a.Value * 2; });
        Assert.Equal(2, doubled.Value);

        a.Value = 2;
        a.Value = 3;
        Assert.Equal(1, computes);

        Assert.Equal(6, doubled.Value);
        Assert.Equal(2, computes);
    }

    [Fact]
    public void Effect_sees_consistent_values_in_a_batch()
    {
        var first = new Signal<string>("Ada");
        var last = new Signal<string>("Lovelace");
        var full = new Computed<string>(() => first.Value + " " + last.Value);
        var seen = new List<string>();
        _ = new Effect(() => seen.Add(full.Value));

        Scheduler.Batch(() =>
        {
            first.Value = "Grace";
            last.Value = "Hopper";
        });

        Assert.Equal(new[] { "Ada Lovelace", "Grace Hopper" }, seen);
    }

    [Fact]
    public void Throwing_computed_is_reported_to_the_effect_owner()
    {
        var a = new Signal<int>(1);
        var risky = new Computed<int>(() => a.Value > 1 ? throw new InvalidOperationException("boom") : a.Value);
        var derived = new Computed<int>(() => risky.Value + 1);
        Exception? reported = null;
        App.OnError = ex => reported = ex;
        _ = new Effect(() => _ = derived.Value);

        a.Value = 2;
        App.OnError = null;

        Assert.IsType<InvalidOperationException>(reported);
    }

    [Fact]
    public void Computed_is_lazy_and_cached()
    {
        var a = new Signal<int>(1);
        var computes = 0;
        var doubled = new Computed<int>(() => { computes++; return a.Value * 2; });

        Assert.Equal(0, computes);
        Assert.Equal(2, doubled.Value);
        Assert.Equal(2, doubled.Value);
        Assert.Equal(1, computes);

        a.Value = 5;
        Assert.Equal(10, doubled.Value);
    }

    [Fact]
    public void Computed_does_not_propagate_equal_results()
    {
        var a = new Signal<int>(1);
        var isPositive = new Computed<bool>(() => a.Value > 0);
        var runs = 0;
        _ = new Effect(() => { _ = isPositive.Value; runs++; });

        a.Value = 2;
        a.Value = 3;
        Assert.Equal(1, runs);

        a.Value = -1;
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Dependencies_are_dynamic()
    {
        var flag = new Signal<bool>(true);
        var left = new Signal<string>("L");
        var right = new Signal<string>("R");
        var runs = 0;
        _ = new Effect(() => { _ = flag.Value ? left.Value : right.Value; runs++; });

        right.Value = "R2";
        Assert.Equal(1, runs);

        flag.Value = false;
        Assert.Equal(2, runs);

        left.Value = "L2";
        Assert.Equal(2, runs);

        right.Value = "R3";
        Assert.Equal(3, runs);
    }

    [Fact]
    public void Disposing_scope_stops_effects()
    {
        var count = new Signal<int>(0);
        var runs = 0;
        var scope = new Scope();
        scope.Run(() => new Effect(() => { _ = count.Value; runs++; }));

        scope.Dispose();
        count.Value = 1;

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Flushed_fires_once_per_outer_batch()
    {
        var flushes = 0;
        Scheduler.Flushed += () => flushes++;
        var a = new Signal<int>(0);

        Scheduler.Batch(() =>
        {
            a.Value = 1;
            Scheduler.Batch(() => a.Value = 2);
        });

        Assert.Equal(1, flushes);
    }
}
