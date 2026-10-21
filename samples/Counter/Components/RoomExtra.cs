using NetWasm.UI.Reactive;

namespace Counter.Components;

/// <summary>Shared state shape for both room demos: an extra item with a counter.</summary>
public sealed class RoomExtra
{
    public RoomExtra(string name, int amount)
    {
        Name = name;
        Amount = new Signal<int>(amount);
    }

    public string Name { get; }

    public Signal<int> Amount { get; }
}
