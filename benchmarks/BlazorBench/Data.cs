using System.Collections.Generic;
using NetWasmBench;

namespace BlazorBench;

public sealed class RowData
{
    public RowData(int id, string label)
    {
        Id = id;
        Label = label;
    }

    public int Id { get; }

    public string Label { get; set; }
}

public static class Data
{
    private static int seed = 42;
    private static int nextId = 1;

    public static List<RowData> Build(int count)
    {
        var rows = new List<RowData>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new RowData(nextId++, Labels.Label(ref seed)));
        }

        return rows;
    }
}
