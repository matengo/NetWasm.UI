using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>Runs a side effect immediately and again whenever a signal read during the previous run changes.</summary>
public sealed class Effect : Computation
{
    private readonly Action action;
    private List<Action>? cleanups;
    private bool scheduled;

    public Effect(Action action)
    {
        this.action = action;
        Tracker.CurrentOwner?.Register(this);
        Run();
    }

    internal void AddCleanup(Action cleanup) => (cleanups ??= new List<Action>()).Add(cleanup);

    private void RunCleanups()
    {
        var list = cleanups;
        if (list is null)
        {
            return;
        }

        cleanups = null;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            Tracker.Untracked(list[i]);
        }
    }

    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        RunCleanups();
        base.Dispose();
    }

    internal void Run()
    {
        scheduled = false;
        if (IsDisposed)
        {
            return;
        }

        RunCleanups();
        ClearSources();
        var previous = Tracker.Current;
        var previousOwner = Tracker.CurrentOwner;
        Tracker.Current = this;
        // Effects re-run from the scheduler; anything they create must pick its owner explicitly.
        Tracker.CurrentOwner = null;
        try
        {
            action();
        }
        finally
        {
            Tracker.Current = previous;
            Tracker.CurrentOwner = previousOwner;
        }
    }

    internal override void MarkDirty()
    {
        if (scheduled || IsDisposed)
        {
            return;
        }

        scheduled = true;
        Scheduler.Enqueue(this);
    }
}
