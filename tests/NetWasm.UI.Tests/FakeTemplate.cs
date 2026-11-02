using System.Net;
using static NetWasm.UI.Tests.FakeDomHost;

namespace NetWasm.UI.Tests;

/// <summary>
/// The fake host's version of dom.mjs <c>defineTemplate</c>/<c>cloneTemplate</c>: a tiny HTML parser that
/// finds the same holes (root, <c>$</c> elements, <c>&lt;!--$--&gt;</c> text, <c>&lt;!--@--&gt;</c> insertion points).
/// </summary>
internal sealed class FakeTemplate
{
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    private readonly FakeNode root;
    private readonly List<FakeNode> holes;

    private FakeTemplate(FakeNode root, List<FakeNode> holes)
    {
        this.root = root;
        this.holes = holes;
    }

    public static FakeTemplate Parse(string html)
    {
        var top = new FakeNode { Kind = "element", Tag = "#fragment" };
        var stack = new Stack<FakeNode>();
        stack.Push(top);
        var holes = new List<FakeNode>();
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                var next = html.IndexOf('<', i);
                var end = next < 0 ? html.Length : next;
                var text = html.Substring(i, end - i);
                i = end;
                if (!(text.Contains('\n') && string.IsNullOrWhiteSpace(text)))
                {
                    Append(stack.Peek(), new FakeNode { Kind = "text", Text = WebUtility.HtmlDecode(text) });
                }

                continue;
            }

            if (html.AsSpan(i).StartsWith("<!--"))
            {
                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                var body = html.Substring(i + 4, end - i - 4);
                i = end + 3;
                if (body is "$" or "@")
                {
                    var hole = body == "$" ? new FakeNode { Kind = "text" } : new FakeNode { Kind = "anchor" };
                    Append(stack.Peek(), hole);
                    holes.Add(hole);
                }

                continue;
            }

            if (html[i + 1] == '/')
            {
                var close = html.IndexOf('>', i);
                var tag = html.Substring(i + 2, close - i - 2).Trim().ToLowerInvariant();
                i = close + 1;
                while (stack.Count > 1 && stack.Pop().Tag != tag)
                {
                }

                continue;
            }

            i++;
            var nameStart = i;
            while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>' && html[i] != '/')
            {
                i++;
            }

            var el = new FakeNode { Kind = "element", Tag = html.Substring(nameStart, i - nameStart).ToLowerInvariant() };
            var marked = false;
            while (html[i] != '>')
            {
                if (char.IsWhiteSpace(html[i]) || html[i] == '/')
                {
                    i++;
                    continue;
                }

                var attrStart = i;
                while (!char.IsWhiteSpace(html[i]) && html[i] != '>' && html[i] != '=' && html[i] != '/')
                {
                    i++;
                }

                var name = html.Substring(attrStart, i - attrStart);
                var value = string.Empty;
                if (html[i] == '=')
                {
                    i++;
                    if (html[i] is '"' or '\'')
                    {
                        var quote = html[i];
                        var endQuote = html.IndexOf(quote, i + 1);
                        value = html.Substring(i + 1, endQuote - i - 1);
                        i = endQuote + 1;
                    }
                    else
                    {
                        var valueStart = i;
                        while (!char.IsWhiteSpace(html[i]) && html[i] != '>')
                        {
                            i++;
                        }

                        value = html.Substring(valueStart, i - valueStart);
                    }
                }

                if (name == "$")
                {
                    marked = true;
                }
                else
                {
                    el.Attributes[name] = WebUtility.HtmlDecode(value);
                }
            }

            i++;
            var isRoot = stack.Count == 1 && top.Children.Count == 0;
            Append(stack.Peek(), el);
            if (marked && !isRoot)
            {
                holes.Add(el);
            }

            if (!VoidTags.Contains(el.Tag))
            {
                stack.Push(el);
            }
        }

        if (top.Children.Count != 1 || top.Children[0].Kind != "element")
        {
            throw new InvalidOperationException("A template needs exactly one root element: " + html);
        }

        var root = top.Children[0];
        root.Parent = null;
        holes.Insert(0, root);
        return new FakeTemplate(root, holes);
    }

    /// <summary>Deep-copies the template and returns the copies of its holes (root first).</summary>
    public List<FakeNode> Clone()
    {
        var map = new Dictionary<FakeNode, FakeNode>();
        Copy(root, null, map);
        return holes.Select(h => map[h]).ToList();
    }

    private static FakeNode Copy(FakeNode node, FakeNode? parent, Dictionary<FakeNode, FakeNode> map)
    {
        var copy = new FakeNode { Kind = node.Kind, Tag = node.Tag, Text = node.Text, Parent = parent };
        foreach (var (k, v) in node.Attributes)
        {
            copy.Attributes[k] = v;
        }

        map[node] = copy;
        foreach (var child in node.Children)
        {
            copy.Children.Add(Copy(child, copy, map));
        }

        return copy;
    }

    private static void Append(FakeNode parent, FakeNode child)
    {
        child.Parent = parent;
        parent.Children.Add(child);
    }
}
