using System;
using System.Collections.Generic;
using NetWasm.UI.Dom;

namespace NetWasm.UI;

/// <summary>
/// Applies a set of attributes and listeners (typically a component's <see cref="Component.Attributes"/>) to an
/// element. Items are applied in order, so later ones win; <c>class</c> and <c>style</c> values are joined with
/// the element's own (<c>baseClass</c>/<c>baseStyle</c>) instead of replacing it. Nested splats are flattened.
/// </summary>
internal sealed class Splat : IContent
{
    private readonly Content[]? items;
    private readonly string? baseClass;
    private readonly string? baseStyle;

    public Splat(Content[]? items, string? baseClass, string? baseStyle)
    {
        this.items = items;
        this.baseClass = baseClass;
        this.baseStyle = baseStyle;
    }

    public void ApplyTo(Element parent)
    {
        List<Attr>? classes = null;
        List<Attr>? styles = null;
        ApplyItems(parent, items, ref classes, ref styles);

        // The element's own class/style is already in the DOM; it only needs rewriting when the splat adds to it.
        if (baseClass is not null)
        {
            classes?.Insert(0, Attr.Static("class", baseClass));
        }

        if (baseStyle is not null)
        {
            styles?.Insert(0, Attr.Static("style", baseStyle));
        }

        Merge(parent, "class", ' ', classes);
        Merge(parent, "style", ';', styles);
    }

    private static void ApplyItems(Element parent, Content[]? items, ref List<Attr>? classes, ref List<Attr>? styles)
    {
        if (items is null)
        {
            return;
        }

        for (var i = 0; i < items.Length; i++)
        {
            switch (items[i].Inner)
            {
                case Splat nested:
                    if (nested.baseClass is not null)
                    {
                        (classes ??= new()).Add(Attr.Static("class", nested.baseClass));
                    }

                    if (nested.baseStyle is not null)
                    {
                        (styles ??= new()).Add(Attr.Static("style", nested.baseStyle));
                    }

                    ApplyItems(parent, nested.items, ref classes, ref styles);
                    break;
                case Attr { Name: "class" } a when a.IsValue:
                    (classes ??= new()).Add(a);
                    break;
                case Attr { Name: "style" } a when a.IsValue:
                    (styles ??= new()).Add(a);
                    break;
                case Listener l:
                    // A listener remembers its handler id, so each application needs its own instance.
                    ((IContent)l.Copy()).ApplyTo(parent);
                    break;
                default:
                    items[i].ApplyTo(parent);
                    break;
            }
        }
    }

    private static void Merge(Element parent, string name, char separator, List<Attr>? parts)
    {
        if (parts is null)
        {
            return;
        }

        var dynamic = false;
        foreach (var p in parts)
        {
            dynamic |= p.DynamicValue is not null;
        }

        if (!dynamic)
        {
            Renderer.SetAttribute(parent.Id, name, Join(parts, separator)!);
            return;
        }

        var snapshot = parts.ToArray();
        _ = new AttrBinding(parent.Id, name, () => Join(snapshot, separator));
    }

    private static string? Join(IReadOnlyList<Attr> parts, char separator)
    {
        string? result = null;
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var value = p.DynamicValue is { } f ? f() : p.StaticValue;
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (result is null)
            {
                result = value;
            }
            else if (result[result.Length - 1] == separator)
            {
                result = string.Concat(result, value);
            }
            else
            {
                result = string.Concat(result, separator.ToString(), value);
            }
        }

        return result ?? string.Empty;
    }
}
