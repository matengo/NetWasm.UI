using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class EventChannelTests : RendererTest
{
    [Fact]
    public void Subscribers_in_components_update_the_dom_and_unsubscribe_on_unmount()
    {
        var messages = new EventChannel<string>();
        App.Mount(new Lambda(() =>
        {
            var last = new Signal<string>("none");
            var count = new Signal<int>(0);
            messages.Subscribe(m => last.Value = m);
            messages.Subscribe(_ => count.Value++);
            return Div(Text(() => $"{last.Value} {count.Value}"));
        }), "#app");

        messages.Publish("a");
        messages.Publish("a");
        Assert.Equal("<div>a 2</div>", Dom.Html);

        App.Unmount();
        Assert.False(messages.HasSubscribers);
    }

    [Fact]
    public void A_channel_registered_as_a_service_reaches_injecting_components()
    {
        var channel = new EventChannel<string>();
        var services = new Services().Add(channel);
        App.Mount(new Lambda(() =>
        {
            var toast = new Signal<string>("");
            Inject<EventChannel<string>>().Subscribe(m => toast.Value = m);
            return P(Text(() => toast.Value));
        }), "#app", services);

        channel.Publish("hello");

        Assert.Equal("<p>hello</p>", Dom.Html);
    }

    [Fact]
    public void One_publish_sends_one_batch()
    {
        var messages = new EventChannel<int>();
        App.Mount(new Lambda(() =>
        {
            var a = new Signal<int>(0);
            var b = new Signal<int>(0);
            messages.Subscribe(v => a.Value = v);
            messages.Subscribe(v => b.Value = v * 2);
            return Div(Text(() => $"{a.Value} {b.Value}"));
        }), "#app");
        var batches = 0;
        Scheduler.Flushed += Count;
        try
        {
            messages.Publish(3);
        }
        finally
        {
            Scheduler.Flushed -= Count;
        }

        Assert.Equal(1, batches);
        Assert.Equal("<div>3 6</div>", Dom.Html);

        void Count() => batches++;
    }

    [Fact]
    public void Unsubscribing_during_publish_skips_that_handler_and_new_subscribers_wait_for_the_next()
    {
        var messages = new EventChannel<int>();
        var seen = new List<string>();
        IDisposable second = null!;
        messages.Subscribe(v =>
        {
            seen.Add($"first {v}");
            second.Dispose();
            messages.Subscribe(w => seen.Add($"late {w}"));
        });
        second = messages.Subscribe(v => seen.Add($"second {v}"));

        messages.Publish(1);

        Assert.Equal(["first 1"], seen);
    }

    [Fact]
    public void A_throwing_handler_goes_to_its_error_boundary_and_the_others_still_run()
    {
        var messages = new EventChannel<string>();
        var others = 0;
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () =>
            {
                messages.Subscribe(m => throw new InvalidOperationException(m));
                return Span("ok");
            },
            e => P("Error: " + e.Message)))), "#app");
        messages.Subscribe(_ => others++);

        messages.Publish("boom");

        Assert.Equal("<div><p>Error: boom</p><!----></div>", Dom.Html);
        Assert.Equal(1, others);
    }

    [Fact]
    public void Handlers_are_untracked_and_effects_resubscribe_on_each_run()
    {
        var messages = new EventChannel<int>();
        var trigger = new Signal<int>(0);
        var read = new Signal<int>(0);
        var effectRuns = 0;
        var handled = new List<int>();
        using var scope = new Scope();
        scope.Run(() =>
        {
            _ = new Effect(() =>
            {
                effectRuns++;
                var t = trigger.Value;
                messages.Subscribe(v => handled.Add(v + t + read.Value));
            });
        });

        trigger.Value = 10;
        messages.Publish(1);
        read.Value = 5;

        Assert.Equal([11], handled);
        Assert.Equal(2, effectRuns);

        scope.Dispose();
        Assert.False(messages.HasSubscribers);
    }

    [Fact]
    public void Publishing_from_an_effect_does_not_track_what_handlers_read()
    {
        var messages = new EventChannel<int>();
        var read = new Signal<int>(0);
        var source = new Signal<int>(0);
        var effectRuns = 0;
        using var sub = messages.Subscribe(_ => _ = read.Value);
        using var scope = new Scope();
        scope.Run(() =>
        {
            _ = new Effect(() =>
            {
                effectRuns++;
                messages.Publish(source.Value);
            });
        });

        read.Value = 1;
        Assert.Equal(1, effectRuns);
        source.Value = 1;
        Assert.Equal(2, effectRuns);
    }

    [Fact]
    public void Outside_a_scope_the_handle_unsubscribes()
    {
        var messages = new EventChannel<int>();
        var total = 0;
        var sub = messages.Subscribe(v => total += v);

        messages.Publish(1);
        sub.Dispose();
        sub.Dispose();
        messages.Publish(2);

        Assert.Equal(1, total);
        Assert.False(messages.HasSubscribers);
        Assert.Throws<ArgumentNullException>(() => messages.Subscribe(null!));
    }

    [Fact]
    public void Nested_publishes_run_immediately()
    {
        var first = new EventChannel<int>();
        var second = new EventChannel<int>();
        var order = new List<string>();
        using var a = first.Subscribe(v =>
        {
            order.Add("first");
            second.Publish(v);
            order.Add("first done");
        });
        using var b = second.Subscribe(_ => order.Add("second"));

        first.Publish(1);

        Assert.Equal(["first", "second", "first done"], order);
    }
}
