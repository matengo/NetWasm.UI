using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class IntervalTests : RendererTest
{
    private readonly Queue<TaskCompletionSource> delays = new();
    private readonly Func<int, Task> realDelay = Interval.Delay;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;

    public IntervalTests()
    {
        // Run continuations inline when a fake delay completes, like the single-threaded browser loop.
        SynchronizationContext.SetSynchronizationContext(null);
        Interval.Delay = _ =>
        {
            var tcs = new TaskCompletionSource();
            delays.Enqueue(tcs);
            return tcs.Task;
        };
    }

    public override void Dispose()
    {
        Interval.Delay = realDelay;
        SynchronizationContext.SetSynchronizationContext(context);
        base.Dispose();
    }

    private void Elapse() => delays.Dequeue().SetResult();

    [Fact]
    public void Ticks_update_the_dom_until_the_component_is_removed()
    {
        App.Mount(new Lambda(() =>
        {
            var count = new Signal<int>(0);
            SetInterval(() => count.Value++, 1000);
            return Div(Text(() => $"Count value is {count.Value}"));
        }), "#app");
        Assert.Equal("<div>Count value is 0</div>", Dom.Html);

        Elapse();
        Elapse();
        Assert.Equal("<div>Count value is 2</div>", Dom.Html);

        var pending = delays.Dequeue();
        App.Unmount();
        pending.SetResult();
        Assert.Empty(delays);
    }

    [Fact]
    public void Disposing_the_handle_stops_the_timer()
    {
        var ticks = 0;
        IDisposable timer = null!;
        App.Mount(new Lambda(() =>
        {
            timer = SetInterval(() => ticks++, 10);
            return P("x");
        }), "#app");

        Elapse();
        timer.Dispose();
        Elapse();

        Assert.Equal(1, ticks);
        Assert.Empty(delays);
    }

    [Fact]
    public void Tick_errors_go_to_the_nearest_error_boundary()
    {
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () =>
            {
                SetInterval(() => throw new InvalidOperationException("tick"), 10);
                return Span("ok");
            },
            e => P("Error: " + e.Message)))), "#app");

        Elapse();

        Assert.Equal("<div><p>Error: tick</p><!----></div>", Dom.Html);
        Assert.Empty(delays);
    }

    [Fact]
    public void Invalid_arguments_and_missing_owner_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => SetInterval(() => { }, 10));
        App.Mount(new Lambda(() =>
        {
            Assert.Throws<ArgumentNullException>(() => SetInterval(null!, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => SetInterval(() => { }, 0));
            return P("x");
        }), "#app");
        Assert.Empty(delays);
    }
}
