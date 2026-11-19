using System.Text.RegularExpressions;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

/// <summary>
/// Rows of a keyed list rendered through <c>TemplateRuntime.Rows</c>, written as the template compiler emits them.
/// </summary>
public class TemplateRowTests : RendererTest
{
    private sealed class Item
    {
        public Item(int id, string label)
        {
            Id = id;
            Label = new Signal<string>(label);
        }

        public int Id { get; }

        public Signal<string> Label { get; }
    }

    // Shows the label its item had when it was created, as a component with plain properties does.
    private sealed class Tag : Component
    {
        private readonly Item item;
        private readonly List<string> log;

        public Tag(Item item, List<string> log)
        {
            this.item = item;
            this.log = log;
        }

        protected override Node Build()
        {
            var label = item.Label.Peek();
            log.Add("built " + label);
            OnCleanup(() => log.Add("gone " + label));
            return Span(label);
        }
    }

    private readonly record struct Point(int Id, int X);

    private sealed class Host : Component
    {
        private readonly Func<Node> build;

        public Host(Func<Node> build) => this.build = build;

        protected override Node Build() => build();
    }

    // hole 0: the <li>, hole 1: the id text, hole 2: the <a>, hole 3: the label text.
    private static Template RowTemplate() => new Template("<li><!--$--> <a $><!--$--></a></li>").Listen(2, "click");

    private readonly Signal<IReadOnlyList<Item>> items = new(Array.Empty<Item>());
    private readonly Signal<int> selected = new(0);
    private readonly List<string> clicks = new();

    private void Mount(Template template, Action<TemplateRow, Item>? setup = null)
    {
        App.Mount(new Host(() => Ul(TemplateRuntime.Rows(() => (IReadOnlySignal<IReadOnlyList<Item>>)items, item => item.Id, template, 3,
            setup,
            (row, item) =>
            {
                row.Attr(0, 0, "class", selected.Value == item.Id ? "on" : null);
                row.Text(1, 1, item.Id);
                row.Text(2, 3, item.Label);
            },
            (row, item, slot, e) =>
            {
                switch (slot)
                {
                    case 0: TemplateRuntime.Invoke(() => clicks.Add(item.Label.Value), e); break;
                }
            }))), "#app");
    }

    private FakeDomHost.FakeNode Link(int index) => Dom.QueryAll("a")[index];

    [Fact]
    public void Renders_a_row_per_item_from_the_template()
    {
        items.Value = [new Item(1, "one"), new Item(2, "two")];
        Mount(RowTemplate());

        Assert.Equal("<ul><li>1 <a>one</a></li><li>2 <a>two</a></li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void A_row_costs_one_clone_and_its_first_writes_and_no_listener_op()
    {
        Mount(RowTemplate());
        Dom.Log.Clear();

        items.Value = [new Item(1, "one"), new Item(2, "two")];

        Assert.Equal(2, Dom.Log.Count(l => l.Op == "CloneTemplate"));
        Assert.DoesNotContain(Dom.Log, l => l.Op == "AddListener");
        // A fresh clone has no class to remove, so the null attribute writes nothing.
        Assert.DoesNotContain(Dom.Log, l => l.Op is "RemoveAttribute" or "SetAttribute");
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "TemplateListener"));
    }

    [Fact]
    public void A_change_reruns_the_row_and_writes_only_what_changed()
    {
        var one = new Item(1, "one");
        items.Value = [one, new Item(2, "two")];
        Mount(RowTemplate());
        Dom.Log.Clear();

        one.Label.Value = "uno";

        Assert.Equal("<ul><li>1 <a>uno</a></li><li>2 <a>two</a></li><!----></ul>", Dom.Html);
        var writes = Dom.Log.Where(l => l.Op is "SetText" or "SetTextInt" or "SetAttribute" or "RemoveAttribute").ToList();
        Assert.Single(writes);
        Assert.Equal("SetText", writes[0].Op);
    }

    [Fact]
    public void An_attribute_is_set_and_removed_as_its_value_changes()
    {
        items.Value = [new Item(1, "one"), new Item(2, "two")];
        Mount(RowTemplate());

        selected.Value = 2;
        Assert.Equal("<ul><li>1 <a>one</a></li><li class=\"on\">2 <a>two</a></li><!----></ul>", Dom.Html);

        selected.Value = 1;
        Assert.Equal("<ul><li class=\"on\">1 <a>one</a></li><li>2 <a>two</a></li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void A_template_listener_reaches_the_row_it_fired_in()
    {
        items.Value = [new Item(1, "one"), new Item(2, "two")];
        Mount(RowTemplate());

        Dom.RowEvent(Link(1), "click");
        Dom.RowEvent(Link(0), "click");

        Assert.Equal(["two", "one"], clicks);
    }

    [Fact]
    public void The_host_dispatches_a_row_event_to_handler_zero_with_the_row_and_slot_before_the_payload()
    {
        items.Value = [new Item(1, "one"), new Item(2, "two")];
        Mount(RowTemplate());
        Fire(Link(1), new DomEvent("click", button: 0, clientX: 3, clientY: 4));

        Assert.Equal(["two"], clicks);
    }

    [Fact]
    public void A_surviving_key_takes_the_new_item_for_its_bindings_and_its_events()
    {
        items.Value = [new Item(1, "one"), new Item(2, "two")];
        Mount(RowTemplate());
        var row = Dom.QueryAll("li")[1];
        Dom.Log.Clear();

        // The same keys with new objects, as after a refetch.
        items.Value = [new Item(1, "one"), new Item(2, "zwei")];

        Assert.Equal("<ul><li>1 <a>one</a></li><li>2 <a>zwei</a></li><!----></ul>", Dom.Html);
        Assert.Same(row, Dom.QueryAll("li")[1]);
        Assert.DoesNotContain(Dom.Log, l => l.Op == "CloneTemplate");
        Assert.Single(Dom.Log, l => l.Op == "SetText");

        Dom.RowEvent(Link(1), "click");
        Assert.Equal(["zwei"], clicks);
    }

    [Fact]
    public void A_row_with_setup_content_is_built_again_when_its_key_gets_a_new_item()
    {
        var log = new List<string>();
        var one = new Item(1, "one");
        var three = new Item(3, "three");
        items.Value = [one, new Item(2, "two"), three];
        Mount(RowTemplate(), (row, item) => row.Instance.With(2, new Tag(item, log)));
        var before = Dom.QueryAll("li");
        log.Clear();
        Dom.Log.Clear();

        // The same keys, and a new object for the second one only.
        items.Value = [one, new Item(2, "zwei"), three];

        // The whole row shows the new item: its bindings and the component its setup created.
        Assert.Equal(
            "<ul><li>1 <a>one<span>one</span></a></li><li>2 <a>zwei<span>zwei</span></a></li><li>3 <a>three<span>three</span></a></li><!----></ul>",
            Dom.Html);
        Assert.Equal(["gone two", "built zwei"], log);
        // It is a new row where the old one was; rows whose item is the same object are not touched.
        var after = Dom.QueryAll("li");
        Assert.Same(before[0], after[0]);
        Assert.NotSame(before[1], after[1]);
        Assert.Same(before[2], after[2]);
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "CloneTemplate"));

        Dom.RowEvent(Link(1), "click");
        Assert.Equal(["zwei"], clicks);
    }

    [Fact]
    public void A_row_whose_item_is_the_same_object_keeps_its_setup_content()
    {
        var log = new List<string>();
        var one = new Item(1, "one");
        var two = new Item(2, "two");
        items.Value = [one, two];
        Mount(RowTemplate(), (row, item) => row.Instance.With(2, new Tag(item, log)));
        var before = Dom.QueryAll("li");
        log.Clear();
        Dom.Log.Clear();

        items.Value = [two, one];

        Assert.Empty(log);
        Assert.Equal([before[1], before[0]], Dom.QueryAll("li"));
        Assert.DoesNotContain(Dom.Log, l => l.Op == "CloneTemplate");
    }

    [Fact]
    public void A_value_item_is_compared_by_value()
    {
        var points = new Signal<IReadOnlyList<Point>>([new Point(1, 10), new Point(2, 20)]);
        var template = new Template("<li><!--$--></li>");
        App.Mount(new Host(() => Ul(TemplateRuntime.Rows(() => (IReadOnlySignal<IReadOnlyList<Point>>)points, point => point.Id, template, 1,
            null,
            (row, point) => row.Text(0, 1, point.X)))), "#app");
        Dom.Log.Clear();

        points.Value = [new Point(1, 10), new Point(2, 20)];
        Assert.DoesNotContain(Dom.Log, l => l.Op == "SetTextInt");

        points.Value = [new Point(1, 10), new Point(2, 21)];
        Assert.Single(Dom.Log, l => l.Op == "SetTextInt");
        Assert.Equal("<ul><li>10</li><li>21</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void A_removed_row_releases_its_nodes_and_takes_no_more_events()
    {
        items.Value = [new Item(1, "one"), new Item(2, "two")];
        Mount(RowTemplate());
        var listener = Link(1).RowListeners.Single();
        Dom.Log.Clear();

        items.Value = [items.Value[0]];

        // The row's four node ids go with one op.
        Assert.Equal("4", Assert.Single(Dom.Log, l => l.Op == "ReleaseBlock").Detail.Split(' ')[1]);
        Assert.DoesNotContain(Dom.Log, l => l.Op == "Release");
        Renderer.DispatchRow(listener.First, listener.Slot, new DomEvent("click"));
        Assert.Empty(clicks);
    }

    [Fact]
    public void A_cleared_list_releases_the_ids_of_all_its_rows_with_one_op()
    {
        items.Value = [new Item(1, "one"), new Item(2, "two"), new Item(3, "three")];
        Mount(RowTemplate());
        Dom.Log.Clear();

        items.Value = [];

        // The rows got their id blocks one after the other, so the three blocks of four ids are one run.
        Assert.Equal("12", Assert.Single(Dom.Log, l => l.Op == "ReleaseBlock").Detail.Split(' ')[1]);
        Assert.Equal("<ul><!----></ul>", Dom.Html);

        // The ids are free again: the next rows use them, and the host has dropped the old nodes.
        items.Value = [new Item(4, "four"), new Item(5, "five")];
        Assert.Equal("<ul><li>4 <a>four</a></li><li>5 <a>five</a></li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void Setup_content_is_applied_once_when_the_row_is_created()
    {
        var one = new Item(1, "one");
        items.Value = [one];
        var applied = 0;
        Mount(RowTemplate(), (row, item) =>
        {
            applied++;
            row.Instance.With(2, Attr("href", "/item/" + item.Id.ToString()));
        });

        one.Label.Value = "uno";

        Assert.Equal(1, applied);
        Assert.Equal("<ul><li>1 <a href=\"/item/1\">uno</a></li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void A_row_without_listeners_or_slots_is_just_a_clone()
    {
        var plain = new Template("<li>static</li>");
        var values = new Signal<IReadOnlyList<int>>([1, 2]);
        App.Mount(new Host(() => Ul(TemplateRuntime.Rows<int>(() => (IReadOnlySignal<IReadOnlyList<int>>)values, plain, 0, null, null))), "#app");

        Assert.Equal("<ul><li>static</li><li>static</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void The_host_rejects_a_template_listener_for_an_event_it_does_not_delegate()
    {
        var scroll = new Template("<li>x</li>").Listen(0, "scroll");
        var one = new Signal<IReadOnlyList<int>>([1]);

        Assert.Throws<InvalidOperationException>(() =>
            App.Mount(new Host(() => Ul(TemplateRuntime.Rows<int>(() => (IReadOnlySignal<IReadOnlyList<int>>)one, scroll, 0, null, null))), "#app"));
    }

    [Fact]
    public void The_host_the_runtime_and_the_template_compiler_agree_on_the_delegated_events()
    {
        using var stream = typeof(TemplateRowTests).Assembly.GetManifestResourceStream("dom.mjs")!;
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();

        // const DELEGATED = new Set([ "click", ... ]);
        var start = script.IndexOf("const DELEGATED = new Set([", StringComparison.Ordinal);
        Assert.True(start >= 0, "dom.mjs no longer declares DELEGATED as a Set literal.");
        var list = script[start..script.IndexOf("]);", start, StringComparison.Ordinal)];
        var host = Regex.Matches(list, "\"([a-z]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.NotEmpty(host);

        Assert.Equal(host.Order(), NetWasm.UI.Generator.TemplateEmitter.DelegatedEvents.Order());
        // Template.CanListen is a pattern, not a list, so ask it about every name the host script quotes: the
        // delegated events and the ones it handles in other ways.
        foreach (var name in Regex.Matches(script, "\"([a-z]+)\"").Select(m => m.Groups[1].Value).Distinct())
        {
            Assert.True(Template.CanListen(name) == host.Contains(name), name);
        }
    }

    [Fact]
    public void Listen_takes_element_holes_before_the_first_clone()
    {
        var template = new Template("<li><a $><!--$--></a></li>");

        Assert.Throws<ArgumentOutOfRangeException>(() => template.Listen(2, "click"));
        Assert.Throws<ArgumentException>(() => template.Listen(1, "click", ListenerFlags.Capture));
        Assert.Throws<ArgumentException>(() => template.Listen(1, "click", ListenerFlags.Passive));
        Assert.True(Template.CanListen("input"));
        Assert.False(Template.CanListen("mouseenter"));

        template.Listen(1, "click", ListenerFlags.PreventDefault);
        var values = new Signal<IReadOnlyList<int>>([1]);
        App.Mount(new Host(() => Ul(TemplateRuntime.Rows<int>(() => (IReadOnlySignal<IReadOnlyList<int>>)values, template, 0, null, null))), "#app");
        Assert.Throws<InvalidOperationException>(() => template.Listen(1, "input"));
        Assert.Equal((byte)ListenerFlags.PreventDefault, Dom.Query("a")!.RowListeners.Single().Flags);
    }
}
