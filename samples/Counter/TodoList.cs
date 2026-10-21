using System.Collections.Generic;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Counter;

public sealed class TodoItem
{
    private static int nextId;

    public TodoItem(string title)
    {
        Id = ++nextId;
        Title = title;
    }

    public int Id { get; }
    public string Title { get; }
    public Signal<bool> Done { get; } = new(false);
}

public sealed class TodoList : Component
{
    private readonly Signal<IReadOnlyList<TodoItem>> items = new(new List<TodoItem>
    {
        new("Build a reactive core"),
        new("Batch DOM ops into one call"),
        new("Ship it"),
    });

    private readonly Signal<string> draft = new(string.Empty);

    protected override Node Build()
    {
        var remaining = new Computed<int>(() =>
        {
            var n = 0;
            foreach (var i in items.Value)
            {
                if (!i.Done.Value)
                {
                    n++;
                }
            }

            return n;
        });

        return Section(
            Id("todos"),
            H2("Todos"),
            Form(
                OnSubmit(_ => Add()),
                Input(Id("new-todo"), Placeholder("What needs doing?"), Spread(Bind(draft))),
                Button(Type("submit"), Disabled(() => draft.Value.Trim().Length == 0), "Add")),
            Ul(Each(items, i => i.Id, RenderItem)),
            P(Class("status"), Text(() => $"{remaining.Value} of {items.Value.Count} remaining")),
            Button(Id("clear-done"), OnClick(ClearDone), "Clear completed"));
    }

    private Node RenderItem(TodoItem item) =>
        Li(
            Class(() => item.Done.Value ? "todo done" : "todo"),
            Label(
                Input(Type("checkbox"), Spread(Bind(item.Done))),
                " ",
                item.Title),
            Button(Class("remove"), Title("Remove"), OnClick(() => Remove(item)), "×"));

    private void Add()
    {
        var title = draft.Value.Trim();
        if (title.Length == 0)
        {
            return;
        }

        var next = new List<TodoItem>(items.Value) { new(title) };
        items.Value = next;
        draft.Value = string.Empty;
    }

    private void Remove(TodoItem item) => items.Value = Filter(i => i != item);

    private void ClearDone() => items.Value = Filter(i => !i.Done.Value);

    private List<TodoItem> Filter(System.Func<TodoItem, bool> keep)
    {
        var result = new List<TodoItem>();
        foreach (var i in items.Value)
        {
            if (keep(i))
            {
                result.Add(i);
            }
        }

        return result;
    }
}
