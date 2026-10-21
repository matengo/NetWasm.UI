using System.Collections.Generic;
using NetWasm.UI.Reactive;

namespace Counter;

/// <summary>
/// App-wide store: a plain class with signals, provided once by <see cref="AppRoot"/> and read with
/// <c>Use(AppState.Context)</c>. Pages are rebuilt on navigation, but this state outlives them.
/// </summary>
public sealed class AppState
{
    public static readonly Context<AppState> Context = new("AppState");

    public Signal<int> Count { get; } = new(0);

    public Signal<IReadOnlyList<TodoItem>> Todos { get; } = new(new List<TodoItem>
    {
        new("Build a reactive core"),
        new("Batch DOM ops into one call"),
        new("Ship it"),
    });

    public Signal<string> GreetDraft { get; } = new("world");
}
