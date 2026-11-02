using System;

namespace NetWasm.UI.Reactive;

/// <summary>
/// Routes exceptions from effects, event handlers and lifecycle callbacks to the nearest
/// <see cref="ErrorBoundary"/> (by walking the owner scope chain), then to <see cref="App.OnError"/>.
/// Unhandled errors are rethrown once the current update has been flushed to the host,
/// so the DOM stays consistent and the error still surfaces in the browser console.
/// </summary>
internal static class ErrorHandling
{
    private static Exception? pending;

    internal static Action<Exception>? Global;

    internal static void Report(Exception error, Scope? owner)
    {
        for (var s = owner; s is not null; s = s.Parent)
        {
            if (s.ErrorHandler is { } handler && !s.IsDisposed)
            {
                handler(error);
                return;
            }
        }

        if (Global is { } global)
        {
            global(error);
            return;
        }

        pending ??= error;
    }

    /// <summary>Rethrows the first unhandled error, if any. Called when the outermost flush is done.</summary>
    internal static void ThrowPending()
    {
        var error = pending;
        if (error is not null)
        {
            pending = null;
            throw error;
        }
    }

    internal static void Reset()
    {
        pending = null;
        Global = null;
    }
}
