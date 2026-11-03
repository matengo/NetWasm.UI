using System.Threading;
using System.Threading.Tasks;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class ResourceTests : RendererTest
{
    /// <summary>Hands out loads the test completes by hand (synchronously, on the test thread).</summary>
    private sealed class Loads
    {
        public List<(string Key, TaskCompletionSource<string> Tcs, CancellationToken Token)> Started { get; } = new();

        public Task<string> Fetch(string key, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            Started.Add((key, tcs, ct));
            return tcs.Task;
        }

        public void Resolve(int i, string value) => Started[i].Tcs.SetResult(value);

        public void Fail(int i, string message) => Started[i].Tcs.SetException(new InvalidOperationException(message));
    }

    [Fact]
    public void Loads_and_exposes_state_as_tracked_reads()
    {
        var loads = new Loads();
        var r = Resource.Create(ct => loads.Fetch("a", ct));
        var seen = new List<string>();
        _ = new Effect(() => seen.Add(r.State + ":" + r.Value));

        Assert.Equal(ResourceState.Pending, r.State);
        Assert.True(r.Loading);

        loads.Resolve(0, "A");

        Assert.Equal(ResourceState.Ready, r.State);
        Assert.False(r.Loading);
        Assert.Equal(new[] { "Pending:", "Ready:A" }, seen);
    }

    [Fact]
    public void Completed_task_is_ready_without_a_pending_state()
    {
        var r = Resource.Create(_ => Task.FromResult(42));

        Assert.Equal(ResourceState.Ready, r.State);
        Assert.Equal(42, r.Value);
    }

    [Fact]
    public void Keyed_resource_reloads_on_source_change_and_ignores_stale_results()
    {
        var loads = new Loads();
        var id = new Signal<string?>("1");
        var r = Resource.Create(() => id.Value, loads.Fetch);

        id.Value = "2";
        Assert.True(loads.Started[0].Token.IsCancellationRequested);

        loads.Resolve(0, "stale");
        Assert.Equal(ResourceState.Pending, r.State);

        loads.Resolve(1, "two");
        Assert.Equal("two", r.Value);

        id.Value = "3";
        Assert.Equal(ResourceState.Refreshing, r.State);
        Assert.Equal("two", r.Latest);
        loads.Resolve(2, "three");
        Assert.Equal("three", r.Value);

        id.Value = null;
        Assert.Equal(ResourceState.Unresolved, r.State);
        Assert.Equal(3, loads.Started.Count);
    }

    [Fact]
    public void Failed_load_sets_error_and_value_throws()
    {
        var loads = new Loads();
        var r = Resource.Create(ct => loads.Fetch("a", ct));

        loads.Fail(0, "nope");

        Assert.Equal(ResourceState.Errored, r.State);
        Assert.Equal("nope", r.Error!.Message);
        var ex = Assert.Throws<ResourceException>(() => r.Value);
        Assert.Equal("nope", ex.Message);
        Assert.Null(r.Latest);
    }

    [Fact]
    public void Synchronous_fetcher_exception_becomes_an_error_state()
    {
        var r = Resource.Create<int>(_ => throw new InvalidOperationException("sync"));

        Assert.Equal(ResourceState.Errored, r.State);
        Assert.Equal("sync", r.Error!.Message);
    }

    [Fact]
    public void Refetch_and_mutate()
    {
        var loads = new Loads();
        var r = Resource.Create(ct => loads.Fetch("a", ct));
        loads.Resolve(0, "first");

        r.Refetch();
        Assert.Equal(ResourceState.Refreshing, r.State);
        r.Mutate("local");
        Assert.True(loads.Started[1].Token.IsCancellationRequested);
        Assert.Equal("local", r.Value);

        loads.Resolve(1, "late");
        Assert.Equal("local", r.Value);
    }

    [Fact]
    public void Disposing_the_owner_cancels_the_load()
    {
        var loads = new Loads();
        var scope = new Scope(null);
        var r = scope.Run(() => Resource.Create(ct => loads.Fetch("a", ct)));

        scope.Dispose();

        Assert.True(loads.Started[0].Token.IsCancellationRequested);
        loads.Resolve(0, "ignored");
        Assert.Equal(ResourceState.Pending, r.State);
    }

    [Fact]
    public void Binding_updates_when_the_load_completes()
    {
        var loads = new Loads();
        App.Mount(new Lambda(() =>
        {
            var r = Resource.Create(ct => loads.Fetch("a", ct));
            return P(Text(() => r.Loading ? "loading" : "got " + r.Value));
        }), "#app");
        Assert.Equal("<p>loading</p>", Dom.Html);

        loads.Resolve(0, "data");

        Assert.Equal("<p>got data</p>", Dom.Html);
    }

    [Fact]
    public void Null_text_renders_empty_without_corrupting_the_batch()
    {
        App.Mount(new Lambda(() => P(Text(() => null!), Span("after"))), "#app");

        Assert.Equal("<p><span>after</span></p>", Dom.Html);
    }

    [Fact]
    public void Suspense_shows_fallback_until_all_resources_loaded()
    {
        var loads = new Loads();
        App.Mount(new Lambda(() => Div(Suspense(
            () =>
            {
                var a = Resource.Create(ct => loads.Fetch("a", ct));
                var b = Resource.Create(ct => loads.Fetch("b", ct));
                return P(Text(() => a.Value + "+" + b.Value));
            },
            () => Span("wait")))), "#app");
        Assert.Equal("<div><span>wait</span><!----></div>", Dom.Html);

        loads.Resolve(1, "B");
        Assert.Equal("<div><span>wait</span><!----></div>", Dom.Html);

        loads.Resolve(0, "A");
        Assert.Equal("<div><p>A+B</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Suspense_keeps_content_on_reload()
    {
        var loads = new Loads();
        var key = new Signal<string?>("1");
        App.Mount(new Lambda(() => Div(Suspense(
            () =>
            {
                var r = Resource.Create(() => key.Value, loads.Fetch);
                return P(Text(() => (r.Loading ? "… " : string.Empty) + r.Latest));
            },
            () => Span("wait")))), "#app");
        loads.Resolve(0, "one");
        Assert.Equal("<div><p>one</p><!----></div>", Dom.Html);

        key.Value = "2";

        Assert.Equal("<div><p>… one</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Suspense_without_pending_loads_renders_content_immediately()
    {
        App.Mount(new Lambda(() => Div(Suspense(
            () =>
            {
                var r = Resource.Create(_ => Task.FromResult("now"));
                return P(Text(() => r.Value!));
            },
            () => Span("wait")))), "#app");

        Assert.Equal("<div><p>now</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Failed_load_inside_suspense_reaches_the_error_boundary()
    {
        var loads = new Loads();
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () => Section(Suspense(
                () =>
                {
                    var r = Resource.Create(ct => loads.Fetch("a", ct));
                    return P(Text(() => r.Value ?? string.Empty));
                },
                () => Span("wait"))),
            e => El("b", "Error: " + e.Message)))), "#app");

        loads.Fail(0, "offline");

        Assert.Equal("<div><b>Error: offline</b><!----></div>", Dom.Html);
    }
}
