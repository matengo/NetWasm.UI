using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class MountLifecycleTests : RendererTest
{
    [Fact]
    public void Unmount_removes_the_tree_and_stops_effects()
    {
        var count = new Signal<int>(0);
        var runs = 0;
        var root = new Lambda(() =>
        {
            _ = new Effect(() => { _ = count.Value; runs++; });
            return Div(Text(() => count.Value));
        });
        App.Mount(root, "#app");
        Assert.Equal("<div>0</div>", Dom.Html);

        App.Unmount();
        count.Value = 1;

        Assert.Equal("", Dom.Html);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Unmount_without_mounted_roots_is_a_no_op()
    {
        var before = Dom.BatchCount;
        App.Unmount();
        Assert.Equal(before, Dom.BatchCount);
    }

    private sealed class ReentrantHost : IDomHost
    {
        public readonly FakeDomHost Inner = new();
        public Action? DuringApply;

        public void ApplyBatch(ReadOnlySpan<byte> ops)
        {
            var copy = ops.ToArray();
            Inner.ApplyBatch(copy);
            var action = DuringApply;
            DuringApply = null;
            action?.Invoke();
            // The bytes we were handed must not have been overwritten by the nested flush.
            Assert.Equal(copy, ops.ToArray());
        }
    }

    [Fact]
    public void Ops_written_while_the_host_applies_a_batch_do_not_overwrite_it()
    {
        Renderer.Reset();
        var host = new ReentrantHost();
        Renderer.Initialize(host);
        var text = new Signal<string>("a");
        App.Mount(new Lambda(() => Div(Text(() => text.Value))), "#app");

        host.Inner.Log.Clear();
        host.DuringApply = () => Scheduler.Batch(() => text.Value = "nested");
        Scheduler.Batch(() => text.Value = "outer");

        Assert.Equal("<div>nested</div>", host.Inner.Html);
        // Each op reaches the host exactly once: the nested flush must not resend the outer batch.
        Assert.Equal(2, host.Inner.Log.Count(l => l.Op == "SetText"));
    }
}
