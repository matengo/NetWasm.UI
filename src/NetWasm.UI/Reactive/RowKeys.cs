namespace NetWasm.UI.Reactive;

/// <summary>
/// A list of template rows, keyed by <typeparamref name="TKey"/>, as a <see cref="Selector{TKey}"/> sees it: a row
/// whose bindings ask the selector about the row's own key is not given a node of its own; the selector tells the
/// list which keys changed and the list re-runs those rows.
/// </summary>
internal interface IRowKeys<TKey>
{
    /// <summary>The key of the row.</summary>
    TKey KeyOf(TemplateRow row);

    /// <summary>The selection moved to or from <paramref name="key"/>: the row with that key binds again.</summary>
    void Changed(TKey key);

    /// <summary>False once the list is gone.</summary>
    bool IsAlive { get; }
}
