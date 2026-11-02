using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class ForTests : RendererTest
{
    private sealed class ListView : Component
    {
        public readonly Signal<IReadOnlyList<int>> Items;

        public ListView(params int[] items) => Items = new Signal<IReadOnlyList<int>>(items);

        protected override Node Build() =>
            Ul(Each(Items, i => i, i => Li(i.ToString())));
    }

    private void Set(ListView view, params int[] items) => view.Items.Value = items;

    private static string Items(string html) =>
        html.Replace("<ul>", "").Replace("</ul>", "").Replace("<!---->", "").Replace("<li>", "").Replace("</li>", ",").TrimEnd(',');

    [Fact]
    public void Renders_initial_items_in_order()
    {
        App.Mount(new ListView(1, 2, 3), "#app");
        Assert.Equal("<ul><li>1</li><li>2</li><li>3</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void Appends_without_touching_existing_nodes()
    {
        var v = new ListView(1, 2);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v, 1, 2, 3);

        Assert.Equal("1,2,3", Items(Dom.Html));
        Assert.DoesNotContain(Dom.Log, l => l.Op == "InsertBefore" && l.Detail.StartsWith("1 ") || l.Op == "Remove");
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "InsertBefore"));
    }

    [Fact]
    public void Prepends_and_removes()
    {
        var v = new ListView(1, 2, 3);
        App.Mount(v, "#app");
        var countBefore = Dom.Nodes.Count;

        Set(v, 0, 1, 3);

        Assert.Equal("0,1,3", Items(Dom.Html));
        // One item removed (li + text = 2 handles) and one added: same handle count.
        Assert.Equal(countBefore, Dom.Nodes.Count);
    }

    [Fact]
    public void Reorders_with_minimal_moves()
    {
        var v = new ListView(1, 2, 3, 4);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v, 4, 1, 2, 3);

        Assert.Equal("4,1,2,3", Items(Dom.Html));
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "InsertBefore"));
        Assert.Empty(Dom.Log.Where(l => l.Op == "CreateElement"));
    }

    [Fact]
    public void Reverses()
    {
        var v = new ListView(1, 2, 3);
        App.Mount(v, "#app");

        Set(v, 3, 2, 1);

        Assert.Equal("3,2,1", Items(Dom.Html));
    }

    [Fact]
    public void Clears_and_refills()
    {
        var v = new ListView(1, 2, 3);
        App.Mount(v, "#app");

        Set(v);
        Assert.Equal("<ul><!----></ul>", Dom.Html);

        Set(v, 7, 8);
        Assert.Equal("7,8", Items(Dom.Html));
    }

    [Fact]
    public void Item_level_reactivity_survives_reorder()
    {
        var a = new Signal<string>("a");
        var b = new Signal<string>("b");
        var items = new Signal<IReadOnlyList<Signal<string>>>(new[] { a, b });
        App.Mount(new Lambda(() => Ul(Each(items, s => s, s => Li(Text(() => s.Value))))), "#app");

        items.Value = new[] { b, a };
        a.Value = "A!";

        Assert.Equal("<ul><li>b</li><li>A!</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void Removed_item_stops_reacting()
    {
        var a = new Signal<string>("a");
        var items = new Signal<IReadOnlyList<Signal<string>>>(new[] { a });
        App.Mount(new Lambda(() => Ul(Each(items, s => s, s => Li(Text(() => s.Value))))), "#app");

        items.Value = Array.Empty<Signal<string>>();
        Dom.Log.Clear();
        a.Value = "changed";

        Assert.Empty(Dom.Log);
    }

    [Fact]
    public void Duplicate_keys_throw()
    {
        var v = new ListView(1, 1);
        Assert.Throws<InvalidOperationException>(() => App.Mount(v, "#app"));
    }

    [Fact]
    public void Duplicate_of_an_unchanged_prefix_key_throws_before_touching_the_dom()
    {
        var v = new ListView(1, 2, 3);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Assert.Throws<InvalidOperationException>(() => Set(v, 1, 2, 1, 3));
        Assert.DoesNotContain(Dom.Log, l => l.Op is "Remove" or "InsertBefore" or "CloneTemplate" or "CreateElement");
    }

    [Fact]
    public void Swapping_two_rows_moves_only_those()
    {
        var v = new ListView(1, 2, 3, 4, 5, 6);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v, 1, 5, 3, 4, 2, 6);

        Assert.Equal("1,5,3,4,2,6", Items(Dom.Html));
        Assert.Equal(2, Dom.Log.Count(l => l.Op == "InsertBefore"));
    }

    [Fact]
    public void Random_updates_keep_dom_order_in_sync()
    {
        var random = new Random(1234);
        var v = new ListView();
        App.Mount(v, "#app");
        for (var round = 0; round < 300; round++)
        {
            // Mix of keep-prefix, keep-suffix, shuffles, inserts and removals.
            var pool = Enumerable.Range(0, 12).Where(_ => random.Next(3) > 0).ToList();
            var next = random.Next(4) switch
            {
                0 => pool,
                1 => pool.OrderBy(_ => random.Next()).ToList(),
                2 => pool.Take(pool.Count / 2).Concat(pool.Skip(pool.Count / 2).OrderBy(_ => random.Next())).ToList(),
                _ => pool.Skip(pool.Count / 2).OrderBy(_ => random.Next()).Concat(pool.Take(pool.Count / 2)).ToList(),
            };

            Set(v, next.ToArray());

            Assert.Equal(string.Join(",", next), Items(Dom.Html));
        }
    }

    private sealed class Lambda : Component
    {
        private readonly Func<Node> build;
        public Lambda(Func<Node> build) => this.build = build;
        protected override Node Build() => build();
    }
}
