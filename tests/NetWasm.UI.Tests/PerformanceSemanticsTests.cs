using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

/// <summary>Observer bookkeeping (O(1) links), <see cref="Selector{TKey}"/>, and render-side dedupe.</summary>
public class PerformanceSemanticsTests : RendererTest
{
    [Fact]
    public void Disposing_some_of_many_observers_keeps_the_rest_subscribed()
    {
        var source = new Signal<int>(0);
        var runs = new int[100];
        var effects = new Effect[100];
        for (var i = 0; i < effects.Length; i++)
        {
            var k = i;
            effects[i] = new Effect(() => { _ = source.Value; runs[k]++; });
        }

        // Dispose every third, in an order that exercises swap-removal from the middle and the end.
        for (var i = 0; i < effects.Length; i += 3)
        {
            effects[i].Dispose();
        }

        source.Value = 1;
        source.Value = 2;

        for (var i = 0; i < runs.Length; i++)
        {
            Assert.Equal(i % 3 == 0 ? 1 : 3, runs[i]);
        }
    }

    [Fact]
    public void Re_tracking_with_several_sources_keeps_links_consistent()
    {
        var a = new Signal<int>(1);
        var b = new Signal<int>(10);
        var seen = new List<int>();
        var others = new List<Effect>();
        for (var i = 0; i < 5; i++)
        {
            others.Add(new Effect(() => { _ = a.Value + b.Value; }));
        }

        // Reads a, b, a: the second read of a is a duplicate link that must also be cleaned up.
        _ = new Effect(() => seen.Add(a.Value + b.Value + a.Value));
        foreach (var e in others)
        {
            e.Dispose();
        }

        a.Value = 2;
        b.Value = 20;

        Assert.Equal(new[] { 12, 14, 24 }, seen);
    }

    [Fact]
    public void Computed_with_many_readers_unsubscribes_cleanly()
    {
        var source = new Signal<int>(1);
        var doubled = new Computed<int>(() => source.Value * 2);
        var scope = new Scope(null);
        var sum = 0;
        scope.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                _ = new Effect(() => sum += doubled.Value);
            }
        });
        var survivor = 0;
        _ = new Effect(() => survivor = doubled.Value);

        scope.Dispose();
        sum = 0;
        source.Value = 5;

        Assert.Equal(0, sum);
        Assert.Equal(10, survivor);
    }

    [Fact]
    public void Selector_reruns_only_previous_and_new_key()
    {
        var selected = new Signal<int>(1);
        var isSelected = new Selector<int>(selected);
        var runs = new int[5];
        var state = new bool[5];
        for (var i = 0; i < 5; i++)
        {
            var k = i;
            _ = new Effect(() => { state[k] = isSelected.Is(k); runs[k]++; });
        }

        selected.Value = 3;

        Assert.Equal(new[] { false, false, false, true, false }, state);
        Assert.Equal(new[] { 1, 2, 1, 2, 1 }, runs);
    }

    [Fact]
    public void Selector_handles_keys_that_lose_and_regain_readers()
    {
        var selected = new Signal<int>(0);
        var isSelected = new Selector<int>(selected);
        var first = new Effect(() => _ = isSelected.Is(7));
        first.Dispose();

        var seen = new List<bool>();
        _ = new Effect(() => seen.Add(isSelected.Is(7)));
        selected.Value = 7;
        selected.Value = 8;

        Assert.Equal(new[] { false, true, false }, seen);
    }

    [Fact]
    public void Selector_stops_tracking_when_its_scope_is_disposed()
    {
        var selected = new Signal<int>(0);
        var scope = new Scope(null);
        var runs = 0;
        scope.Run(() =>
        {
            var isSelected = new Selector<int>(selected);
            _ = new Effect(() => { _ = isSelected.Is(1); runs++; });
        });

        scope.Dispose();
        selected.Value = 1;

        Assert.Equal(1, runs);
    }

    private sealed class Toggle : Component
    {
        public readonly Signal<string?> Cls = new("a");

        protected override Node Build() => Div(Class(() => Cls.Value));
    }

    [Fact]
    public void Dynamic_attribute_only_emits_ops_when_the_value_changes()
    {
        var view = new Toggle();
        App.Mount(view, "#app");
        Dom.Log.Clear();

        view.Cls.Value = "a ";  // changed
        view.Cls.Value = null;  // removed
        Assert.Equal(new[] { "SetAttribute", "RemoveAttribute" }, Dom.Log.Select(l => l.Op).ToArray());
    }

    private sealed class NullClass : Component
    {
        protected override Node Build() => Div(Class(() => null));
    }

    [Fact]
    public void Dynamic_attribute_that_starts_null_emits_nothing()
    {
        App.Mount(new NullClass(), "#app");

        Assert.DoesNotContain(Dom.Log, l => l.Op == "RemoveAttribute");
        Assert.Equal("<div></div>", Dom.Html);
    }

    private sealed class ListInShow : Component
    {
        public readonly Signal<bool> Visible = new(true);
        public readonly Signal<IReadOnlyList<int>> Items = new(new[] { 1, 2, 3 });

        protected override Node Build() =>
            Div(When(() => Visible.Value, () => Ul(Each(Items, i => i, i => Li(i.ToString())))));
    }

    [Fact]
    public void List_items_are_released_when_the_owner_goes_away()
    {
        var view = new ListInShow();
        App.Mount(view, "#app");
        var before = Dom.Nodes.Count;
        view.Items.Value = new[] { 1, 2, 3, 4 };
        view.Items.Value = new[] { 4 };

        view.Visible.Value = false;

        // Everything under the <ul> (ul, anchor, remaining li + text) is released; removed items already were.
        Assert.Equal(before - 1 - 1 - 3 * 2, Dom.Nodes.Count);
        Assert.Equal("<div><!----></div>", Dom.Html);
    }
}
