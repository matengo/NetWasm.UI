using System;

namespace NetWasm.UI.Reactive;

/// <summary>Component/effect lifecycle hooks. Also exposed as <c>Html.OnMount</c> / <c>Html.OnCleanup</c>.</summary>
public static class Lifecycle
{
    /// <summary>
    /// Registers <paramref name="cleanup"/> to run when the current owner goes away.
    /// Inside an <see cref="Effect"/> it runs before the effect's next run and when the effect is disposed;
    /// while building it runs when the component/branch scope is disposed (reverse registration order).
    /// </summary>
    public static void OnCleanup(Action cleanup)
    {
        if (Tracker.Current is Effect effect)
        {
            effect.AddCleanup(cleanup);
            return;
        }

        var scope = Tracker.CurrentOwner ?? throw new InvalidOperationException(
            "OnCleanup must be called while building a component or inside an Effect.");
        scope.Register(new ActionDisposable(cleanup));
    }

    /// <summary>
    /// Runs <paramref name="callback"/> once, after the DOM ops of the current update have been applied
    /// by the host (so the nodes exist in the document). Skipped if the owning scope is disposed first.
    /// Signal writes inside the callback are batched into one follow-up update.
    /// </summary>
    public static void OnMount(Action callback)
    {
        var scope = Tracker.CurrentOwner;
        Scheduler.AfterFlush(() =>
        {
            if (scope is null || !scope.IsDisposed)
            {
                Tracker.Untracked(callback);
            }
        });
    }

    private sealed class ActionDisposable : IDisposable
    {
        private Action? action;

        public ActionDisposable(Action action) => this.action = action;

        public void Dispose()
        {
            var a = action;
            action = null;
            if (a is not null)
            {
                Tracker.Untracked(a);
            }
        }
    }
}
