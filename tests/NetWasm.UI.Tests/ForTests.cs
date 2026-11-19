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

    [Theory]
    [InlineData(new[] { 9 }, "1,9,5")]          // middle replaced: range ends at the suffix item
    [InlineData(new int[0], "1,5")]              // middle dropped
    public void Drops_a_run_of_items_with_one_range_op(int[] middle, string expected)
    {
        var v = new ListView(1, 2, 3, 4, 5);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v, [1, .. middle, 5]);

        Assert.Equal(expected, Items(Dom.Html));
        Assert.Single(Dom.Log, l => l.Op == "RemoveRange");
        Assert.DoesNotContain(Dom.Log, l => l.Op == "Remove");
    }

    [Fact]
    public void Removes_items_one_by_one_when_some_of_the_middle_survives()
    {
        var v = new ListView(1, 2, 3, 4);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v, 3, 1);

        Assert.Equal("3,1", Items(Dom.Html));
        Assert.Equal(2, Dom.Log.Count(l => l.Op == "Remove"));
        Assert.DoesNotContain(Dom.Log, l => l.Op == "RemoveRange");
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
    public void Duplicate_of_a_kept_key_in_a_list_that_replaces_the_rest_throws_before_touching_the_dom()
    {
        var v = new ListView(1, 2, 3);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        // 1 stays where it is, 2 and 3 go and nothing of them survives; the second 1 is the duplicate.
        Assert.Throws<InvalidOperationException>(() => Set(v, 1, 8, 1, 9));
        Assert.DoesNotContain(Dom.Log, l => l.Op is "Remove" or "RemoveRange" or "InsertBefore" or "CloneTemplate" or "CreateElement");
    }

    [Fact]
    public void A_list_replaced_by_new_keys_detaches_the_old_rows_with_one_op_and_builds_the_new_ones()
    {
        var v = new ListView(1, 2, 3);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v, 7, 8, 9, 10);

        Assert.Equal("7,8,9,10", Items(Dom.Html));
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "RemoveRange"));
        Assert.DoesNotContain(Dom.Log, l => l.Op == "Remove");
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

    [Fact]
    public void Animated_exit_marks_items_open_and_removes_them_one_by_one()
    {
        var items = new Signal<IReadOnlyList<int>>(new[] { 1, 2, 3 });
        App.Mount(new Lambda(() => Ul(Each(items, i => i, i => Li(i.ToString())).AnimateExit())), "#app");
        Assert.Equal(3, Dom.Html.Split("data-state=\"open\"").Length - 1);

        Dom.Log.Clear();
        items.Value = new[] { 4, 5 };
        Assert.Equal(3, Dom.Log.Count(l => l.Op == "RemoveAnimated"));
        Assert.DoesNotContain(Dom.Log, l => l.Op is "Remove" or "RemoveRange");

        Dom.Log.Clear();
        items.Value = Array.Empty<int>();
        Assert.Equal(2, Dom.Log.Count(l => l.Op == "RemoveAnimated"));
        Assert.DoesNotContain(Dom.Log, l => l.Op is "Remove" or "RemoveRange");
        Assert.Equal("<ul><!----></ul>", Dom.Html);
    }

    [Fact]
    public void Animated_exit_on_a_branch_marks_it_open_and_removes_it_with_the_animated_op()
    {
        var visible = new Signal<bool>(true);
        App.Mount(new Lambda(() => Div(When(() => visible.Value, () => P("x"), () => Span("y")).AnimateExit())), "#app");
        Assert.Contains("<p data-state=\"open\">x</p>", Dom.Html);

        Dom.Log.Clear();
        visible.Value = false;
        Assert.Single(Dom.Log, l => l.Op == "RemoveAnimated");
        Assert.DoesNotContain(Dom.Log, l => l.Op == "Remove");
        Assert.Contains("<span data-state=\"open\">y</span>", Dom.Html);
        Assert.DoesNotContain("<p", Dom.Html);
    }

    [Fact]
    public void Swapping_two_distant_items_moves_those_two_and_touches_nothing_else()
    {
        var v = new ListView(1, 2, 3, 4, 5, 6);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v, 1, 5, 3, 4, 2, 6);

        Assert.Equal("1,5,3,4,2,6", Items(Dom.Html));
        Assert.Equal(2, Dom.Log.Count(l => l.Op == "InsertBefore"));
        Assert.DoesNotContain(Dom.Log, l => l.Op is "Remove" or "RemoveRange" or "CreateElement" or "Release");
    }

    [Fact]
    public void Swapping_two_neighbours_moves_one()
    {
        var v = new ListView(1, 2, 3, 4);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v, 1, 3, 2, 4);

        Assert.Equal("1,3,2,4", Items(Dom.Html));
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "InsertBefore"));
    }

    [Fact]
    public void An_empty_list_detaches_all_items_with_one_op_and_a_later_list_starts_over()
    {
        var v = new ListView(1, 2, 3);
        App.Mount(v, "#app");
        Dom.Log.Clear();

        Set(v);

        Assert.Equal("", Items(Dom.Html));
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "RemoveRange"));
        Assert.DoesNotContain(Dom.Log, l => l.Op == "Remove");

        Set(v, 3, 1);
        Assert.Equal("3,1", Items(Dom.Html));
    }

    [Fact]
    public void The_dom_follows_the_list_through_a_long_sequence_of_changes()
    {
        // The list keeps its keys and lookup tables from one change to the next; a long seeded sequence of
        // inserts, removals, moves, swaps, reversals and clears must leave the DOM equal to the list every time.
        var random = new Random(20261117);
        var items = new List<int>();
        var next = 1;
        var v = new ListView();
        App.Mount(v, "#app");

        for (var step = 0; step < 400; step++)
        {
            switch (random.Next(8))
            {
                case 0:
                case 1:
                    items.Insert(random.Next(items.Count + 1), next++);
                    break;
                case 2 when items.Count > 0:
                    items.RemoveAt(random.Next(items.Count));
                    break;
                case 3 when items.Count > 1:
                {
                    var a = random.Next(items.Count);
                    var b = random.Next(items.Count);
                    (items[a], items[b]) = (items[b], items[a]);
                    break;
                }
                case 4 when items.Count > 1:
                {
                    var from = random.Next(items.Count);
                    var item = items[from];
                    items.RemoveAt(from);
                    items.Insert(random.Next(items.Count + 1), item);
                    break;
                }
                case 5 when items.Count > 2:
                    items.Reverse();
                    break;
                case 6 when random.Next(6) == 0:
                    items.Clear();
                    break;
                default:
                    for (var i = random.Next(4); i >= 0; i--)
                    {
                        items.Add(next++);
                    }

                    break;
            }

            Set(v, items.ToArray());
            Assert.Equal(string.Join(",", items), Items(Dom.Html));
        }
    }

    private sealed class Person
    {
        public Person(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }
        public string Name { get; }
    }

    private readonly record struct Point(int Id, string Label);

    private static Signal<IReadOnlyList<Person>> MountPeople(params Person[] people)
    {
        var items = new Signal<IReadOnlyList<Person>>(people);
        App.Mount(new Lambda(() => Ul(Each(items, p => p.Id, p => Li(p.Name)))), "#app");
        return items;
    }

    [Fact]
    public void A_key_with_a_new_item_is_rendered_again_in_place_and_the_others_are_kept()
    {
        var ann = new Person(1, "Ann");
        var cid = new Person(3, "Cid");
        var items = MountPeople(ann, new Person(2, "Bo"), cid);
        var kept = Dom.Nodes.Count;
        Dom.Log.Clear();

        items.Value = new[] { ann, new Person(2, "Bob"), cid };

        Assert.Equal("Ann,Bob,Cid", Items(Dom.Html));
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "CreateElement"));
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "Remove"));
        Assert.Equal(kept, Dom.Nodes.Count);
    }

    [Fact]
    public void The_same_items_are_not_rendered_again()
    {
        var people = new[] { new Person(1, "Ann"), new Person(2, "Bo"), new Person(3, "Cid") };
        var items = MountPeople(people);
        Dom.Log.Clear();

        items.Value = new[] { people[2], people[0], people[1] };

        Assert.Equal("Cid,Ann,Bo", Items(Dom.Html));
        Assert.DoesNotContain(Dom.Log, l => l.Op is "CreateElement" or "Remove");
    }

    [Fact]
    public void New_items_and_a_new_order_in_one_update()
    {
        var items = MountPeople(new Person(1, "Ann"), new Person(2, "Bo"), new Person(3, "Cid"), new Person(4, "Dag"));

        items.Value = new[] { new Person(4, "Dag!"), new Person(2, "Bo!"), new Person(5, "Eva"), new Person(1, "Ann!") };

        Assert.Equal("Dag!,Bo!,Eva,Ann!", Items(Dom.Html));
    }

    [Fact]
    public void A_replaced_item_stops_reacting()
    {
        var name = new Signal<string>("Ann");
        var first = new Person(1, "Ann");
        var items = new Signal<IReadOnlyList<Person>>(new[] { first });
        App.Mount(new Lambda(() => Ul(Each(items, p => p.Id, p => Li(Text(() => p == first ? name.Value : p.Name))))), "#app");

        items.Value = new[] { new Person(1, "Bo") };
        Dom.Log.Clear();
        name.Value = "changed";

        Assert.Equal("Bo", Items(Dom.Html));
        Assert.Empty(Dom.Log);
    }

    [Fact]
    public void A_value_type_item_is_rendered_again_only_when_its_value_changes()
    {
        var items = new Signal<IReadOnlyList<Point>>(new[] { new Point(1, "a"), new Point(2, "b") });
        App.Mount(new Lambda(() => Ul(Each(items, p => p.Id, p => Li(p.Label)))), "#app");
        Dom.Log.Clear();

        items.Value = new[] { new Point(1, "a"), new Point(2, "b") };
        Assert.DoesNotContain(Dom.Log, l => l.Op == "CreateElement");

        items.Value = new[] { new Point(1, "a"), new Point(2, "B") };
        Assert.Equal("a,B", Items(Dom.Html));
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "CreateElement"));
    }

    [Fact]
    public void Items_that_are_their_own_keys_are_kept_when_an_equal_item_comes()
    {
        var items = new Signal<IReadOnlyList<string>>(new[] { "a", "b" });
        App.Mount(new Lambda(() => Ul(TemplateRuntime.Each(() => items, s => Li(s)))), "#app");
        Dom.Log.Clear();

        items.Value = new[] { new string('a', 1), new string('b', 1) };

        Assert.Equal("a,b", Items(Dom.Html));
        Assert.DoesNotContain(Dom.Log, l => l.Op is "CreateElement" or "Remove");
    }
}
