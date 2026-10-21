using NetWasm.UI.Reactive;

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
