using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

/// <summary>
/// Template rows whose bindings ask a <see cref="Selector{TKey}"/> about the row's own key, written as the template
/// compiler emits <c>class="@(isSelected.Is(row.Id) ? "on" : null)"</c> on a row with <c>@key="row.Id"</c>.
/// </summary>
public class SelectorRowKeyTests : RendererTest
{
    private sealed class Item
    {
        public Item(int id, string label)
        {
            Id = id;
            Label = label;
        }

        public int Id { get; }

        public string Label { get; }
    }

    private sealed class Host : Component
    {
        private readonly Func<Node> build;

        public Host(Func<Node> build) => this.build = build;

        protected override Node Build() => build();
    }

    private readonly Signal<IReadOnlyList<Item>> items = new(Array.Empty<Item>());
    private readonly Signal<IReadOnlyList<Item>> others = new(Array.Empty<Item>());
    private readonly Signal<bool> showOthers = new(true);
    private readonly Signal<int> selected = new(0);
    private readonly List<int> binds = new();
    private Selector<int> isSelected = null!;

    // hole 0: the <li>, hole 1: the label text.
    private static Template RowTemplate() => new("<li><!--$--></li>");

    private For<Item, int> List(Signal<IReadOnlyList<Item>> source, Func<Item, int> askAbout) =>
        TemplateRuntime.Rows(() => (IReadOnlySignal<IReadOnlyList<Item>>)source, item => item.Id, RowTemplate(), 2, null,
            (row, item) =>
            {
                binds.Add(item.Id);
                row.Attr(0, 0, "class", isSelected.Is(askAbout(item)) ? "on" : null);
                row.Text(1, 1, item.Label);
            });

    private void Mount(Func<Item, int>? askAbout = null, bool twoLists = false)
    {
        App.Mount(new Host(() =>
        {
            isSelected = new Selector<int>(selected);
            var ask = askAbout ?? (item => item.Id);
            return twoLists
                ? Div(Ul(List(items, ask)), new Show(() => showOthers.Value, () => Ol(List(others, ask))))
                : Ul(List(items, ask));
        }), "#app");
    }

    private static Item[] Items(params int[] ids) => ids.Select(id => new Item(id, "r" + id)).ToArray();

    [Fact]
    public void Moving_the_selection_binds_only_the_previous_and_the_new_row()
    {
        items.Value = Items(1, 2, 3, 4);
        Mount();
        binds.Clear();

        selected.Value = 2;
        Assert.Equal([2], binds);
        Assert.Equal("<ul><li>r1</li><li class=\"on\">r2</li><li>r3</li><li>r4</li><!----></ul>", Dom.Html);

        binds.Clear();
        selected.Value = 4;
        Assert.Equal([2, 4], binds.Order());
        Assert.Equal("<ul><li>r1</li><li>r2</li><li>r3</li><li class=\"on\">r4</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void Selecting_a_key_that_is_not_in_the_list_only_unselects_the_previous_row()
    {
        items.Value = Items(1, 2);
        Mount();
        selected.Value = 1;
        binds.Clear();

        selected.Value = 99;

        Assert.Equal([1], binds);
        Assert.Equal("<ul><li>r1</li><li>r2</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void A_removed_and_re_added_row_follows_the_selection()
    {
        items.Value = Items(1, 2, 3);
        Mount();
        selected.Value = 2;

        items.Value = Items(1, 3);
        selected.Value = 3;
        Assert.Equal("<ul><li>r1</li><li class=\"on\">r3</li><!----></ul>", Dom.Html);

        items.Value = Items(1, 2, 3);
        selected.Value = 2;
        Assert.Equal("<ul><li>r1</li><li class=\"on\">r2</li><li>r3</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void A_row_given_a_new_item_for_its_key_follows_the_selection()
    {
        items.Value = Items(1, 2);
        Mount();
        selected.Value = 1;

        // New objects with the same keys: the rows take them in place.
        items.Value = [new Item(1, "one"), new Item(2, "two")];
        Assert.Equal("<ul><li class=\"on\">one</li><li>two</li><!----></ul>", Dom.Html);

        selected.Value = 2;
        Assert.Equal("<ul><li>one</li><li class=\"on\">two</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void A_row_that_asks_about_another_key_is_tracked_as_before()
    {
        items.Value = Items(1, 2, 3);
        // Each row is "on" when the row after it is selected.
        Mount(item => item.Id + 1);
        binds.Clear();

        selected.Value = 3;

        Assert.Equal([2], binds);
        Assert.Equal("<ul><li>r1</li><li class=\"on\">r2</li><li>r3</li><!----></ul>", Dom.Html);
    }

    [Fact]
    public void A_selector_with_its_own_comparer_still_reaches_every_row_it_matched()
    {
        // Keys that only a custom comparer finds equal: the row keyed 12 matches a selection of 2 (by last digit).
        items.Value = Items(1, 12);
        App.Mount(new Host(() =>
        {
            isSelected = new Selector<int>(selected, new LastDigit());
            return Ul(List(items, item => item.Id));
        }), "#app");

        selected.Value = 2;
        Assert.Equal("<ul><li>r1</li><li class=\"on\">r12</li><!----></ul>", Dom.Html);

        selected.Value = 1;
        Assert.Equal("<ul><li class=\"on\">r1</li><li>r12</li><!----></ul>", Dom.Html);
    }

    private sealed class LastDigit : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => x % 10 == y % 10;

        public int GetHashCode(int obj) => obj % 10;
    }

    [Fact]
    public void Two_lists_can_share_a_selector()
    {
        items.Value = Items(1, 2);
        others.Value = Items(2, 3);
        Mount(twoLists: true);

        selected.Value = 2;
        Assert.Equal("<div><ul><li>r1</li><li class=\"on\">r2</li><!----></ul><ol><li class=\"on\">r2</li><li>r3</li><!----></ol><!----></div>", Dom.Html);

        selected.Value = 3;
        Assert.Equal("<div><ul><li>r1</li><li>r2</li><!----></ul><ol><li>r2</li><li class=\"on\">r3</li><!----></ol><!----></div>", Dom.Html);
    }

    [Fact]
    public void A_list_that_is_gone_is_no_longer_told_about_the_selection()
    {
        items.Value = Items(1, 2);
        others.Value = Items(1, 2);
        Mount(twoLists: true);
        selected.Value = 1;

        showOthers.Value = false;
        binds.Clear();
        selected.Value = 2;

        // Only the remaining list's two rows bind again.
        Assert.Equal([1, 2], binds.Order());
        Assert.Equal("<div><ul><li>r1</li><li class=\"on\">r2</li><!----></ul><!----></div>", Dom.Html);
    }
}
