using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>
/// Single-threaded scheduler. Signal writes inside a <see cref="Batch"/> queue effects and
/// run them once when the outermost batch ends; writes outside a batch flush immediately.
/// <see cref="Flushed"/> fires after each flush so the DOM layer can send one interop batch.
/// </summary>
public static class Scheduler
{
    private static readonly Queue<Effect> queue = new();
    private static readonly Queue<Action> afterFlush = new();
    private static int depth;
    private static int flushDepth;
    private static bool flushing;
    private static bool flushRequested;

    /// <summary>Raised after queued effects ran. The DOM renderer uses this to push its op batch.</summary>
    public static event Action? Flushed
    {
        add => flushed.Add(value!);
        remove => flushed.Remove(value!);
    }

    // A list instead of a field-like event: avoids pulling multicast delegate code into the Wasm module.
    private static readonly List<Action> flushed = new();

    public static bool InBatch => depth > 0;

    internal static void Enqueue(Effect effect) => queue.Enqueue(effect);

    /// <summary>Queues <paramref name="action"/> to run (in its own batch) after the next flush has reached the host.</summary>
    internal static void AfterFlush(Action action) => afterFlush.Enqueue(action);

    public static void Batch(Action action)
    {
        BeginBatch();
        try
        {
            action();
        }
        finally
        {
            EndBatch();
        }
    }

    // Allocation-free form of Batch for hot paths (every signal write).
    internal static void BeginBatch() => depth++;

    internal static void EndBatch()
    {
        depth--;
        if (depth == 0)
        {
            Flush();
        }
    }

    public static void Flush()
    {
        if (flushing)
        {
            // Re-entered from a Flushed handler (an event the host dispatched while applying a batch):
            // the running flush picks the new work up before it returns.
            flushRequested = true;
            return;
        }

        flushDepth++;
        try
        {
            FlushCore();
        }
        finally
        {
            flushDepth--;
        }

        if (flushDepth == 0 && depth == 0)
        {
            ErrorHandling.ThrowPending();
        }
    }

    private static void FlushCore()
    {
        flushing = true;
        try
        {
            // Effects may enqueue further effects; drain until stable. A Flushed handler may re-enter
            // (see Flush), in which case effects and output are drained again.
            var guard = 0;
            do
            {
                flushRequested = false;
                while (queue.Count > 0)
                {
                    if (++guard > 100_000)
                    {
                        throw new InvalidOperationException("Reactive update cycle detected.");
                    }

                    queue.Dequeue().Run();
                }

                foreach (var handler in flushed.ToArray())
                {
                    handler();
                }

                if (++guard > 100_000)
                {
                    throw new InvalidOperationException("Reactive update cycle detected.");
                }
            }
            while (flushRequested || queue.Count > 0);
        }
        finally
        {
            flushing = false;
            flushRequested = false;
        }

        // OnMount callbacks: the DOM is up to date now. Each runs in its own batch so its writes
        // flush (and may enqueue further callbacks, drained by the nested Flush).
        var mountGuard = 0;
        while (afterFlush.Count > 0)
        {
            if (++mountGuard > 100_000)
            {
                throw new InvalidOperationException("OnMount cycle detected.");
            }

            Batch(afterFlush.Dequeue());
        }
    }

    /// <summary>Clears all state. Intended for tests.</summary>
    internal static void Reset()
    {
        queue.Clear();
        afterFlush.Clear();
        depth = 0;
        flushDepth = 0;
        flushing = false;
        flushRequested = false;
        flushed.Clear();
        ErrorHandling.Reset();
    }
}
