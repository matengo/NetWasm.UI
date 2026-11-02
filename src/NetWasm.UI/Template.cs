using System;
using System.Collections.Generic;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Static HTML that the host parses once and then clones, so a whole subtree costs one op instead of
/// one per node and attribute. Mark the places code needs to reach as <em>holes</em>:
/// <list type="bullet">
/// <item><c>&lt;a $&gt;</c> – an element (attributes, listeners or children are added in code),</item>
/// <item><c>&lt;!--$--&gt;</c> – a text node (static or reactive text),</item>
/// <item><c>&lt;!--@--&gt;</c> – an insertion point (components, <c>When</c>, <c>Each</c>, any content).</item>
/// </list>
/// Hole 0 is always the root element; the marked holes follow in document order. Keep the instance in a
/// <c>static readonly</c> field so it is defined once.
/// </summary>
/// <example>
/// <code>
/// static readonly Template Row = new("&lt;tr&gt;&lt;td class=\"id\"&gt;&lt;!--$--&gt;&lt;/td&gt;&lt;td&gt;&lt;a $&gt;&lt;!--$--&gt;&lt;/a&gt;&lt;/td&gt;&lt;/tr&gt;");
///
/// Node RowView(Row row) => Row.Clone()
///     .Text(1, row.Id.ToString())
///     .With(2, OnClick(() => Select(row)))
///     .Text(3, () => row.Label.Value)
///     .Root;
/// </code>
/// </example>
public sealed class Template
{
    internal const byte ElementHole = 0;
    internal const byte TextHole = 1;
    internal const byte AnchorHole = 2;

    private static int nextId = 1;

    private readonly byte[] kinds;
    private readonly string[] tags;
    private int definedIn = -1;

    public Template(string html)
    {
        Html = html ?? throw new ArgumentNullException(nameof(html));
        Id = nextId++;
        Scan(html, out kinds, out tags);
    }

    internal int Id { get; }

    internal string Html { get; }

    /// <summary>Number of holes including the root (hole 0).</summary>
    public int HoleCount => kinds.Length;

    /// <summary>Clones the template into the DOM and returns handles to its holes.</summary>
    public TemplateInstance Clone()
    {
        if (definedIn != Renderer.Generation)
        {
            Renderer.DefineTemplate(Id, Html);
            definedIn = Renderer.Generation;
        }

        var first = Renderer.CloneTemplate(Id, kinds.Length);
        var instance = new TemplateInstance(first, kinds, tags);
        Scope.Current?.Register(instance);
        return instance;
    }

    /// <summary>
    /// Finds the holes the same way the host does: the first element is the root, then every element with a
    /// <c>$</c> attribute and every <c>&lt;!--$--&gt;</c> / <c>&lt;!--@--&gt;</c> comment, in source order.
    /// </summary>
    /// <remarks>
    /// ASCII-only and without culture-aware or case-insensitive string APIs: those pull Unicode tables
    /// into the Wasm, which would cost far more than this scanner.
    /// </remarks>
    private static void Scan(string html, out byte[] kinds, out string[] tags)
    {
        kinds = new byte[8];
        tags = new string[8];
        var count = 0;
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                i++;
                continue;
            }

            if (At(html, i, "<!--"))
            {
                var end = Find(html, "-->", i + 4);
                if (end < 0)
                {
                    throw new ArgumentException("Template: unterminated comment.", nameof(html));
                }

                if (end == i + 5 && html[i + 4] is '$' or '@')
                {
                    if (count == 0)
                    {
                        throw new ArgumentException("Template: a hole cannot come before the root element.", nameof(html));
                    }

                    Add(ref kinds, ref tags, ref count, html[i + 4] == '$' ? TextHole : AnchorHole, string.Empty);
                }

                i = end + 3;
                continue;
            }

            if (i + 1 >= html.Length || !IsAsciiLetter(html[i + 1]))
            {
                // End tag, doctype or stray '<'.
                var close = html.IndexOf('>', i + 1);
                i = close < 0 ? html.Length : close + 1;
                continue;
            }

            var nameStart = i + 1;
            i = nameStart;
            while (i < html.Length && !IsTagEnd(html[i]))
            {
                i++;
            }

            var tag = LowerAscii(html.Substring(nameStart, i - nameStart));
            var isRoot = count == 0;
            if (isRoot)
            {
                Add(ref kinds, ref tags, ref count, ElementHole, tag);
            }

            // Attributes.
            while (i < html.Length && html[i] != '>')
            {
                var c = html[i];
                if (IsSpace(c) || c == '/')
                {
                    i++;
                    continue;
                }

                var attrStart = i;
                while (i < html.Length && !IsTagEnd(html[i]) && html[i] != '=')
                {
                    i++;
                }

                var isMarker = i - attrStart == 1 && html[attrStart] == '$';
                while (i < html.Length && IsSpace(html[i]))
                {
                    i++;
                }

                if (i < html.Length && html[i] == '=')
                {
                    i++;
                    while (i < html.Length && IsSpace(html[i]))
                    {
                        i++;
                    }

                    if (i < html.Length && html[i] is '"' or '\'')
                    {
                        var endQuote = html.IndexOf(html[i], i + 1);
                        i = endQuote < 0 ? html.Length : endQuote + 1;
                    }
                    else
                    {
                        while (i < html.Length && !IsSpace(html[i]) && html[i] != '>')
                        {
                            i++;
                        }
                    }
                }

                if (isMarker && !isRoot)
                {
                    Add(ref kinds, ref tags, ref count, ElementHole, tag);
                }
            }

            i++;
            if (tag is "script" or "style" or "textarea" or "title")
            {
                i = IndexOfEndTag(html, tag, i);
            }
        }

        if (count == 0)
        {
            throw new ArgumentException("Template: the HTML must contain a root element.", nameof(html));
        }

        if (count != kinds.Length)
        {
            var k = new byte[count];
            var t = new string[count];
            Array.Copy(kinds, k, count);
            Array.Copy(tags, t, count);
            kinds = k;
            tags = t;
        }
    }

    private static void Add(ref byte[] kinds, ref string[] tags, ref int count, byte kind, string tag)
    {
        if (count == kinds.Length)
        {
            var k = new byte[count * 2];
            var t = new string[count * 2];
            Array.Copy(kinds, k, count);
            Array.Copy(tags, t, count);
            kinds = k;
            tags = t;
        }

        kinds[count] = kind;
        tags[count] = tag;
        count++;
    }

    private static bool At(string s, int index, string value)
    {
        if (index + value.Length > s.Length)
        {
            return false;
        }

        for (var j = 0; j < value.Length; j++)
        {
            if (s[index + j] != value[j])
            {
                return false;
            }
        }

        return true;
    }

    private static int Find(string s, string value, int from)
    {
        for (var i = s.IndexOf(value[0], from); i >= 0; i = s.IndexOf(value[0], i + 1))
        {
            if (At(s, i, value))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsTagEnd(char c) => IsSpace(c) || c == '>' || c == '/';

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static bool IsAsciiLetter(char c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';

    private static string LowerAscii(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] is >= 'A' and <= 'Z')
            {
                var chars = s.ToCharArray();
                for (var j = i; j < chars.Length; j++)
                {
                    if (chars[j] is >= 'A' and <= 'Z')
                    {
                        chars[j] = (char)(chars[j] | 0x20);
                    }
                }

                return new string(chars);
            }
        }

        return s;
    }

    /// <summary>Index of <c>&lt;/tag</c> (any case) at or after <paramref name="from"/>, or the end of the string.</summary>
    private static int IndexOfEndTag(string html, string tag, int from)
    {
        for (var i = Find(html, "</", from); i >= 0; i = Find(html, "</", i + 2))
        {
            var j = 0;
            while (j < tag.Length && i + 2 + j < html.Length && (char)(html[i + 2 + j] | 0x20) == tag[j])
            {
                j++;
            }

            if (j == tag.Length)
            {
                return i;
            }
        }

        return html.Length;
    }
    internal byte KindOf(int hole) => kinds[hole];
}

/// <summary>
/// A cloned <see cref="Template"/>: handles to its holes. Hole 0 is <see cref="Root"/>. Owned by the scope it
/// was cloned in, which releases all its nodes at once.
/// </summary>
public sealed class TemplateInstance : IDisposable
{
    // Hole i has node id first + i. Element handles are created on demand; text holes never need one.
    private readonly int first;
    private readonly byte[] kinds;
    private readonly string[] tags;
    private Element? root;

    internal TemplateInstance(int first, byte[] kinds, string[] tags)
    {
        this.first = first;
        this.kinds = kinds;
        this.tags = tags;
    }

    /// <summary>The root element (hole 0).</summary>
    public Element Root => root ??= new Element(first, tags[0]);

    /// <summary>The element marked with <c>$</c> at <paramref name="hole"/>.</summary>
    public Element Element(int hole)
    {
        Expect(hole, Template.ElementHole, "an element ('<tag $>')");
        return hole == 0 ? Root : new Element(first + hole, tags[hole]);
    }

    /// <summary>Adds attributes, listeners or children to an element hole, or inserts content at a <c>&lt;!--@--&gt;</c> hole.</summary>
    public TemplateInstance With(int hole, params Content[] content)
    {
        if (hole < 0 || hole >= kinds.Length || kinds[hole] == Template.TextHole)
        {
            throw new ArgumentOutOfRangeException(nameof(hole), "Hole " + hole.ToString() + " is not an element or insertion point.");
        }

        // An insertion point is a comment; the host turns "append to it" into "insert before it".
        var target = hole == 0 ? Root : new Element(first + hole, tags[hole]);
        for (var i = 0; i < content.Length; i++)
        {
            content[i].ApplyTo(target);
        }

        return this;
    }

    /// <summary>Sets the text of a <c>&lt;!--$--&gt;</c> hole.</summary>
    public TemplateInstance Text(int hole, string text)
    {
        Renderer.SetText(Expect(hole, Template.TextHole, "a text hole ('<!--$-->')"), text);
        return this;
    }

    /// <summary>Binds a <c>&lt;!--$--&gt;</c> hole to reactive text.</summary>
    public TemplateInstance Text(int hole, Func<string> text)
    {
        _ = new TextBinding(Expect(hole, Template.TextHole, "a text hole ('<!--$-->')"), text);
        return this;
    }

    /// <summary>Binds a <c>&lt;!--$--&gt;</c> hole to any value, formatted with the invariant culture.</summary>
    public TemplateInstance Text<T>(int hole, Func<T> value) =>
        Text(hole, () => TemplateRuntime.Str(value()) ?? string.Empty);

    /// <summary>Hands out an element hole (e.g. to keep a reference for <c>Focus()</c>).</summary>
    public TemplateInstance Ref(int hole, out Element element)
    {
        element = Element(hole);
        return this;
    }

    /// <summary>Releases the host nodes of all holes. Called by the owning scope.</summary>
    public void Dispose()
    {
        for (var i = 0; i < kinds.Length; i++)
        {
            Renderer.Release(first + i);
        }
    }

    /// <summary>Returns the node id of <paramref name="hole"/> after checking its kind.</summary>
    private int Expect(int hole, byte kind, string what)
    {
        if (hole < 0 || hole >= kinds.Length || kinds[hole] != kind)
        {
            throw new ArgumentOutOfRangeException(nameof(hole), "Hole " + hole.ToString() + " is not " + what + ".");
        }

        return first + hole;
    }
}
