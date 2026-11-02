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

/// <summary>
/// Row labels as in js-framework-benchmark. The random generator is a fixed LCG that
/// <c>benchmarks/solid/src/data.js</c> implements identically, so both apps render the same rows.
/// </summary>
public static class Data
{
    private static readonly string[] Adjectives =
    [
        "pretty", "large", "big", "small", "tall", "short", "long", "handsome", "plain", "quaint", "clean",
        "elegant", "easy", "angry", "crazy", "helpful", "mushy", "odd", "unsightly", "adorable", "important",
        "inexpensive", "cheap", "expensive", "fancy",
    ];

    private static readonly string[] Colours =
        ["red", "yellow", "blue", "green", "pink", "brown", "purple", "brown", "white", "black", "orange"];

    private static readonly string[] Nouns =
        ["table", "chair", "house", "bbq", "desk", "car", "pony", "cookie", "sandwich", "burger", "pizza", "mouse", "keyboard"];

    private static int seed = 42;
    private static int nextId = 1;

    public static int Random(ref int state, int max)
    {
        state = unchecked(state * 1103515245 + 12345) & 0x7fffffff;
        return state % max;
    }

    public static string Label(ref int state) =>
        Adjectives[Random(ref state, Adjectives.Length)] + " " + Colours[Random(ref state, Colours.Length)] + " " +
        Nouns[Random(ref state, Nouns.Length)];

    public static List<Row> Build(int count)
    {
        var rows = new List<Row>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new Row(nextId++, Label(ref seed)));
        }

        return rows;
    }
}
