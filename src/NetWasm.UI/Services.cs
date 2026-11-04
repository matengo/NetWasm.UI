using System;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Services to make available to a component tree, keyed by type and resolved with
/// <see cref="Html.Inject{T}"/>. Pass them to <c>BrowserApp.Start</c>/<see cref="App.Mount(Component, string, Services)"/>
/// for app-wide services, or to <see cref="Html.Provide(Services)"/> for one component and its descendants.
/// <code>
/// var services = new Services()
///     .Add&lt;IClock&gt;(new SystemClock())          // an existing instance
///     .AddLazy&lt;IApi&gt;(() => new HttpApi(Inject&lt;IClock&gt;())); // created on first Inject
/// </code>
/// </summary>
/// <remarks>
/// <para>A service is registered under exactly <c>T</c>: <c>Add(new HttpApi())</c> registers <c>HttpApi</c>, not
/// <c>IApi</c>, so give the type argument when consumers inject an interface.</para>
/// <para>There is no reflection: the key is a per-type static object, and lookups walk the scope tree like
/// <see cref="Context{T}"/>. The nearest provider wins, so a subtree can override an app-wide service.</para>
/// <para>Lifetimes follow the scope that provides the set. <see cref="AddLazy{T}"/> creates one instance per
/// providing scope (app-wide when passed to <c>Start</c>, per subtree when provided in a component) and disposes
/// it with that scope if it is <see cref="IDisposable"/>. Instances passed to <see cref="Add{T}"/> belong to the
/// caller and are never disposed. For a new instance on every resolve, register a factory: <c>Add&lt;Func&lt;Foo&gt;&gt;(() => new Foo())</c>.</para>
/// </remarks>
public sealed class Services
{
    // A linked list in registration order: avoids a List<T> instantiation in the Wasm.
    private Registration? first;
    private Registration? last;

    /// <summary>Registers <paramref name="instance"/> as the <typeparamref name="T"/> service.</summary>
    public Services Add<T>(T instance)
    {
        if (instance is null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        Append(new InstanceRegistration<T>(instance));
        return this;
    }

    /// <summary>
    /// Registers a <typeparamref name="T"/> service that <paramref name="factory"/> creates on first
    /// <see cref="Html.Inject{T}"/>. The factory runs in the providing scope, so it can inject services
    /// provided there or above it.
    /// </summary>
    public Services AddLazy<T>(Func<T> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        Append(new LazyRegistration<T>(factory));
        return this;
    }

    private void Append(Registration registration)
    {
        if (last is null)
        {
            first = registration;
        }
        else
        {
            last.Next = registration;
        }

        last = registration;
    }

    internal void ApplyTo(Scope scope)
    {
        for (var r = first; r is not null; r = r.Next)
        {
            r.Apply(scope);
        }
    }

    internal static bool TryResolve<T>(out T value)
    {
        if (Scope.Current is { } scope && scope.TryGetContext(Key<T>.Value, out var entry))
        {
            value = ((Entry<T>)entry!).Get();
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>One identity object per service type; generic statics need no reflection.</summary>
    private static class Key<T>
    {
        public static readonly object Value = new();
    }

    private abstract class Registration
    {
        public Registration? Next;

        public abstract void Apply(Scope scope);
    }

    private sealed class InstanceRegistration<T> : Registration
    {
        private readonly T instance;

        public InstanceRegistration(T instance) => this.instance = instance;

        public override void Apply(Scope scope) => scope.SetContext(Key<T>.Value, new Entry<T>(instance));
    }

    private sealed class LazyRegistration<T> : Registration
    {
        private readonly Func<T> factory;

        public LazyRegistration(Func<T> factory) => this.factory = factory;

        public override void Apply(Scope scope)
        {
            var entry = new Entry<T>(factory, scope);
            scope.SetContext(Key<T>.Value, entry);
            // Registered before anything built in the scope, so it is disposed after its consumers.
            scope.Register(entry);
        }
    }

    private sealed class Entry<T> : IDisposable
    {
        private readonly Scope? owner;
        private Func<T>? factory;
        private T value;
        private bool creating;
        private bool disposed;

        public Entry(T value)
        {
            this.value = value;
        }

        public Entry(Func<T> factory, Scope owner)
        {
            this.factory = factory;
            this.owner = owner;
            value = default!;
        }

        public T Get()
        {
            if (disposed)
            {
                throw new InvalidOperationException("The scope that provided this service has been disposed.");
            }

            var create = factory;
            if (create is null)
            {
                return value;
            }

            if (creating)
            {
                throw new InvalidOperationException("Circular service dependency: a service factory injected the service it is creating.");
            }

            creating = true;
            try
            {
                value = owner!.Run(() => Tracker.Untracked(create));
            }
            finally
            {
                creating = false;
            }

            factory = null;
            return value;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (factory is null && value is IDisposable d)
            {
                d.Dispose();
            }

            factory = null;
            value = default!;
        }
    }
}
