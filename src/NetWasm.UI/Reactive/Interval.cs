using System;
using System.Threading.Tasks;

namespace NetWasm.UI.Reactive;

/// <summary>A repeating timer owned by a component scope or effect run. Created by <c>Html.SetInterval</c>.</summary>
internal sealed class Interval : IDisposable
{
    /// <summary>The wait between ticks; tests replace it with a controllable delay.</summary>
    internal static Func<int, Task> Delay = static milliseconds => Task.Delay(milliseconds);

    private readonly Action tick;
    private readonly int milliseconds;
    private readonly Scope? owner;
    private bool stopped;

    private Interval(Action tick, int milliseconds, Scope? owner)
    {
        this.tick = tick;
        this.milliseconds = milliseconds;
        this.owner = owner;
    }

    internal static Interval Start(Action tick, int milliseconds)
    {
        if (tick is null)
        {
            throw new ArgumentNullException(nameof(tick));
        }

        if (milliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "The interval must be positive.");
        }

        var interval = new Interval(tick, milliseconds, Tracker.CurrentOwner);
        Lifecycle.OnCleanup(interval.Dispose);
        _ = interval.Run();
        return interval;
    }

    public void Dispose() => stopped = true;

    private bool IsStopped => stopped || owner is { IsDisposed: true };

    private async Task Run()
    {
        while (!IsStopped)
        {
            await Delay(milliseconds);
            if (IsStopped)
            {
                return;
            }

            try
            {
                Scheduler.Batch(() => Tracker.Untracked(tick));
            }
            catch (Exception ex)
            {
                ErrorHandling.Report(ex, owner);
            }
        }
    }
}
