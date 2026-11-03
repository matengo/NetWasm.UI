using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>
/// Runs a side effect immediately and again whenever a signal read during the previous run changes.
/// An exception from the first run propagates to the caller (so a failing build reaches the enclosing
/// <see cref="ErrorBoundary"/>); exceptions from later runs are reported to the owner scope's boundary.
/// </summary>
public class Effect : Computation
{
    private readonly Action? action;
    private readonly Scope? owner;
    private List<Action>? cleanups;
    private bool scheduled;
    private bool started;

    public Effect(Action action)
        : this()
    {
        this.action = action;
        Run();
    }

    /// <summary>For built-in bindings that override <see cref="Execute"/> instead of wrapping a closure. Call <see cref="Run"/> last in the constructor.</summary>
    private protected Effect()
    {
        owner = Tracker.CurrentOwner;
        owner?.Register(this);
    }

    private protected virtual void Execute() => action!();

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
            try
            {
                Tracker.Untracked(list[i]);
            }
            catch (Exception ex)
            {
                ErrorHandling.Report(ex, null);
            }
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

        // Only reached through a derived value: re-run only if one of them actually changed.
        if (started && State == Check)
        {
            ResolveGuarded();
            if (State == Clean)
            {
                return;
            }
        }

        State = Clean;
        RunCleanups();
        ClearSources();
        var previous = Tracker.Current;
        var previousOwner = Tracker.CurrentOwner;
        Tracker.Current = this;
        // Effects re-run from the scheduler; anything they create must pick its owner explicitly.
        Tracker.CurrentOwner = null;
        var first = !started;
        started = true;
        try
        {
            if (first)
            {
                Execute();
            }
            else
            {
                RunGuarded();
            }
        }
        finally
        {
            Tracker.Current = previous;
            Tracker.CurrentOwner = previousOwner;
        }
    }

    private void RunGuarded()
    {
        try
        {
            Execute();
        }
        catch (Exception ex)
        {
            ErrorHandling.Report(ex, owner);
        }
    }

    // A throwing derived value makes the effect re-run, so the exception surfaces (and is reported) when it reads it.
    private void ResolveGuarded()
    {
        try
        {
            ResolveCheck();
        }
        catch (Exception)
        {
            State = Dirty;
        }
    }

    internal override void MarkDirty()
    {
        if (IsDisposed)
        {
            return;
        }

        State = Dirty;
        Schedule();
    }

    internal override void MarkCheck()
    {
        if (IsDisposed)
        {
            return;
        }

        if (State == Clean)
        {
            State = Check;
        }

        Schedule();
    }

    private void Schedule()
    {
        if (scheduled)
        {
            return;
        }

        scheduled = true;
        Scheduler.Enqueue(this);
    }
}
