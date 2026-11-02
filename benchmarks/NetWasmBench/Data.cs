using System.Collections.Generic;
using NetWasm.UI.Reactive;

namespace NetWasmBench;

public sealed class Row
{
    public Row(int id, string label)
    {
        Id = id;
        Label = new Signal<string>(label);
    }

    public int Id { get; }

    public Signal<string> Label { get; }
}

public static class Data
{
    private static int seed = 42;
    private static int nextId = 1;

    public static List<Row> Build(int count)
    {
        var rows = new List<Row>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new Row(nextId++, Labels.Label(ref seed)));
        }

        return rows;
    }
}
