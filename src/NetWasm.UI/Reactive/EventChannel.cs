using System;

namespace NetWasm.UI.Reactive;

/// <summary>
/// A typed, synchronous event channel. Publishers call <see cref="Publish"/>; every subscriber's handler
/// runs right away, in subscription order, inside one batch. Use a <see cref="Signal{T}"/> for state
/// and a channel for things that happen (each <see cref="Publish"/> is delivered, even with an equal value).
/// </summary>
public sealed class EventChannel<T>
{
    private Subscription[] subscriptions = [];

    /// <summary>True while at least one handler is subscribed.</summary>
    public bool HasSubscribers => subscriptions.Length > 0;

    /// <summary>
    /// Subscribes <paramref name="handler"/>. Called while building a component or branch, the
    /// subscription ends when that scope is disposed; inside an <see cref="Effect"/>, before its next run.
    /// Elsewhere (in a service, for example), dispose the returned handle to unsubscribe.
    /// </summary>
    public IDisposable Subscribe(Action<T> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var subscription = new Subscription(this, handler, Tracker.CurrentOwner);
        subscriptions = [.. subscriptions, subscription];
        if (Tracker.Current is Effect || Tracker.CurrentOwner is not null)
        {
            Lifecycle.OnCleanup(subscription.Dispose);
        }

        return subscription;
    }

    /// <summary>
    /// Calls every subscribed handler with <paramref name="message"/>, untracked and in one batch.
    /// A handler that throws is reported to the error boundary of the scope it subscribed in;
    /// the other handlers still run. Handlers that subscribe during the call get the next message.
    /// </summary>
    public void Publish(T message)
    {
        var snapshot = subscriptions;
        if (snapshot.Length == 0)
        {
            return;
        }

        Scheduler.BeginBatch();
        var previous = Tracker.Current;
        Tracker.Current = null;
        try
        {
            foreach (var subscription in snapshot)
            {
                subscription.Invoke(message);
            }
        }
        finally
        {
            Tracker.Current = previous;
            Scheduler.EndBatch();
        }
    }

    private void Remove(Subscription subscription)
    {
        var current = subscriptions;
        var index = Array.IndexOf(current, subscription);
        if (index < 0)
        {
            return;
        }

        var next = new Subscription[current.Length - 1];
        Array.Copy(current, 0, next, 0, index);
        Array.Copy(current, index + 1, next, index, next.Length - index);
        subscriptions = next;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly EventChannel<T> channel;
        private readonly Scope? owner;
        private Action<T>? handler;

        public Subscription(EventChannel<T> channel, Action<T> handler, Scope? owner)
        {
            this.channel = channel;
            this.handler = handler;
            this.owner = owner;
        }

        public void Invoke(T message)
        {
            // Unsubscribed earlier in the same Publish, or its scope is gone.
            var h = handler;
            if (h is null || owner is { IsDisposed: true })
            {
                return;
            }

            try
            {
                h(message);
            }
            catch (Exception ex)
            {
                ErrorHandling.Report(ex, owner);
            }
        }

        public void Dispose()
        {
            if (handler is null)
            {
                return;
            }

            handler = null;
            channel.Remove(this);
        }
    }
}
