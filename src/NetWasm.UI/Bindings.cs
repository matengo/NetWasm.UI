using System;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

// Effects for the common bindings. Each is one object instead of an Effect plus a closure plus its
// captured-variables class, which matters when a list builds thousands of rows.

/// <summary>Keeps a text node's content in sync with <c>text()</c>.</summary>
internal sealed class TextBinding : Effect
{
    private readonly int id;
    private readonly Func<string> text;

    public TextBinding(int id, Func<string> text)
    {
        this.id = id;
        this.text = text;
        Run();
    }

    private protected override void Execute() => Renderer.SetText(id, text());
}

/// <summary>Keeps an attribute in sync with <c>value()</c>; <c>null</c> removes it.</summary>
internal sealed class AttrBinding : Effect
{
    private readonly int id;
    private readonly string name;
    private readonly Func<string?> value;
    private string? last;

    public AttrBinding(int id, string name, Func<string?> value)
    {
        this.id = id;
        this.name = name;
        this.value = value;
        Run();
    }

    private protected override void Execute()
    {
        // Only touch the DOM when the value changed; a fresh element has no attribute yet.
        var v = value();
        if (v == last)
        {
            return;
        }

        last = v;
        if (v is null)
        {
            Renderer.RemoveAttribute(id, name);
        }
        else
        {
            Renderer.SetAttribute(id, name, v);
        }
    }
}
