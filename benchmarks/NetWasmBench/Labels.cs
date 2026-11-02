namespace NetWasmBench;

/// <summary>
/// Row labels as in js-framework-benchmark, from a fixed LCG that <c>benchmarks/solid/src/data.js</c>
/// implements identically, so every app renders the same rows. Also linked into BlazorBench.
/// </summary>
public static class Labels
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

    public static int Random(ref int state, int max)
    {
        state = unchecked(state * 1103515245 + 12345) & 0x7fffffff;
        return state % max;
    }

    public static string Label(ref int state) =>
        Adjectives[Random(ref state, Adjectives.Length)] + " " + Colours[Random(ref state, Colours.Length)] + " " +
        Nouns[Random(ref state, Nouns.Length)];
}
