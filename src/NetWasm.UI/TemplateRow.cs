using System;
using System.Collections.Generic;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// One row of a list that <c>TemplateRuntime.Rows</c> renders from a template. The row is also the effect that
/// keeps its bindings up to date: the template compiler emits all bindings of a row as one function of the row,
/// so a row needs no closure, delegate or effect per binding. It remembers what each binding wrote and writes
/// again only when the value changed.
/// </summary>
public sealed class TemplateRow : Effect
{
    // What a slot last wrote: a string, or one of the markers below (a number is kept beside its marker).
    private struct Written
    {
        public object? Value;
        public int Number;
    }

    private static readonly object IsNumber = new();
    private static readonly object IsRemoved = new();

    private readonly RowBindings bindings;
    private readonly int first;
    private readonly Written[]? last;
    private TemplateInstance? instance;
    private RowNode? root;

    internal TemplateRow(RowBindings bindings, object? item, int first)
    {
        this.bindings = bindings;
        this.first = first;
        Item = item;
        if (bindings.SlotCount > 0)
        {
            last = new Written[bindings.SlotCount];
        }

        if (bindings.HasEvents)
        {
            Renderer.TrackRow(first, this);
        }
    }

    internal object? Item { get; private set; }

    internal RowNode Root => root ??= new RowNode(first, this);

    // The list gave this row's key a different item: the bindings run for it now, and later events get it.
    // Only for a row that is nothing but bindings and template listeners; a row with setup content is built again.
    internal void Take(object? item)
    {
        Item = item;
        Run();
    }

    /// <summary>The row's clone, for content that is applied once when the row is created.</summary>
    public TemplateInstance Instance => instance ??= bindings.Template.InstanceAt(first);

    internal void Start() => Run();

    internal string ListenerEvent(int slot) => bindings.Template.ListenerEvent(slot);

    // As Renderer.Invoke does for a listener: inside a batch, and errors go to the nearest ErrorBoundary.
    internal void Handle(int slot, DomEvent e) => Scheduler.Batch(() =>
    {
        try
        {
            bindings.Handle(this, slot, e);
        }
        catch (Exception ex)
        {
            ErrorHandling.Report(ex, Owner);
        }
    });

    private protected override void Execute() => bindings.Bind(this);

    /// <summary>Writes a text hole, unless it already shows this text.</summary>
    public void Text(int slot, int hole, string? value)
    {
        var text = value ?? string.Empty;
        ref var written = ref last![slot];
        if (written.Value is string previous && previous == text)
        {
            return;
        }

        written.Value = text;
        Renderer.SetText(first + hole, text);
    }

    /// <summary>Writes a number into a text hole, unless it already shows this number.</summary>
    public void Text(int slot, int hole, int value)
    {
        ref var written = ref last![slot];
        if (written.Value == IsNumber && written.Number == value)
        {
            return;
        }

        written.Value = IsNumber;
        written.Number = value;
        Renderer.SetTextInt(first + hole, value);
    }

    /// <summary>Writes any value into a text hole, as <see cref="TemplateRuntime.Str{T}"/> prints it.</summary>
    public void Text<T>(int slot, int hole, T value) => Text(slot, hole, TemplateRuntime.Str(value));

    /// <summary>Sets an attribute of a hole's element, or removes it when the value is null.</summary>
    public void Attr(int slot, int hole, string name, string? value)
    {
        ref var written = ref last![slot];
        if (value is null)
        {
            // A fresh clone has no attribute to remove.
            if (written.Value is null || written.Value == IsRemoved)
            {
                return;
            }

            written.Value = IsRemoved;
            Renderer.RemoveAttribute(first + hole, name);
            return;
        }

        if (written.Value is string previous && previous == value)
        {
            return;
        }

        written.Value = value;
        Renderer.SetAttribute(first + hole, name, value);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (!IsDisposed)
        {
            if (bindings.HasEvents)
            {
                Renderer.ForgetRow(first);
            }

            Renderer.ReleaseBlock(first, bindings.Template.HoleCount);
        }

        base.Dispose();
    }
}

/// <summary>What every row of one list shares: its template and the functions that fill a row in.</summary>
internal abstract class RowBindings
{
    protected RowBindings(Template template, int slotCount)
    {
        Template = template;
        SlotCount = slotCount;
    }

    internal Template Template { get; }

    internal int SlotCount { get; }

    internal abstract bool HasEvents { get; }

    internal abstract void Bind(TemplateRow row);

    internal abstract void Handle(TemplateRow row, int slot, DomEvent e);
}

internal sealed class RowBindings<T> : RowBindings
{
    private readonly Action<TemplateRow, T>? setup;
    private readonly Action<TemplateRow, T>? bind;
    private readonly Action<TemplateRow, T, int, DomEvent>? events;

    internal RowBindings(Template template, int slotCount, Action<TemplateRow, T>? setup, Action<TemplateRow, T>? bind,
        Action<TemplateRow, T, int, DomEvent>? events)
        : base(template, slotCount)
    {
        this.setup = setup;
        this.bind = bind;
        this.events = events;
    }

    internal override bool HasEvents => events is not null;

    internal override void Handle(TemplateRow row, int slot, DomEvent e) => events!(row, (T)row.Item!, slot, e);

    // A key that survives a new list keeps its place. When the list has a different item for that key (new
    // objects with the same keys, as after a refetch), the whole row shows the new item: a row that is nothing but
    // bindings and template listeners takes it in place, and a row with setup content, which was applied once
    // for the old item, is built again.
    internal void Refresh(KeyedList list, IReadOnlyList<T> items, Func<int, Node> render)
    {
        var count = items.Count;
        for (var i = 0; i < count; i++)
        {
            var row = ((RowNode)list.NodeAt(i)).Row;
            var item = items[i];
            if (KeyedList.SameItem((T)row.Item!, item))
            {
                continue;
            }

            if (setup is null)
            {
                row.Take(item);
            }
            else
            {
                list.Replace(i, render, null);
            }
        }
    }

    internal Node Render(T item)
    {
        var row = new TemplateRow(this, item, Template.CloneBlock());
        setup?.Invoke(row, item);
        row.Start();
        return row.Root;
    }

    internal override void Bind(TemplateRow row) => bind?.Invoke(row, (T)row.Item!);
}

/// <summary>A row's root node, as the keyed list holds it. The row releases the node ids.</summary>
internal sealed class RowNode : Node
{
    internal RowNode(int id, TemplateRow row)
        : base(id, owned: true)
    {
        Row = row;
    }

    internal TemplateRow Row { get; }
}
