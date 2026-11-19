namespace NetWasm.UI.Testing;

// A small CSS selector subset: compounds of tag, #id, .class, [attr] and [attr=value] (quoted or not), joined by the
// descendant combinator (whitespace).
internal sealed class Selector
{
    private readonly List<Compound> parts;

    private Selector(List<Compound> parts) => this.parts = parts;

    public static Selector Parse(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        var parts = new List<Compound>();
        var pos = 0;
        while (true)
        {
            while (pos < selector.Length && char.IsWhiteSpace(selector[pos]))
            {
                pos++;
            }

            if (pos == selector.Length)
            {
                break;
            }

            parts.Add(Compound.Parse(selector, ref pos));
        }

        return new Selector(parts);
    }

    public IReadOnlyList<TestElement> FindAll(FakeDomHost.FakeNode scope, FakeDomHost.FakeNode document) =>
        scope.Descendants().Where(n => n.Kind == "element" && Matches(n, parts.Count - 1, scope)).Select(n => new TestElement(n, document)).ToArray();

    private bool Matches(FakeDomHost.FakeNode node, int part, FakeDomHost.FakeNode scope)
    {
        if (!parts[part].Matches(node))
        {
            return false;
        }

        if (part == 0)
        {
            return true;
        }

        for (var a = node.Parent; a is not null && a != scope; a = a.Parent)
        {
            if (a.Kind == "element" && Matches(a, part - 1, scope))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Compound
    {
        private string? tag;
        private readonly List<string> classes = new();
        private readonly List<(string Name, string? Value)> attributes = new();

        public static Compound Parse(string s, ref int pos)
        {
            var c = new Compound();
            var start = pos;
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]))
            {
                switch (s[pos])
                {
                    case '#':
                        pos++;
                        c.attributes.Add(("id", Name(s, ref pos)));
                        break;
                    case '.':
                        pos++;
                        c.classes.Add(Name(s, ref pos));
                        break;
                    case '[':
                        pos++;
                        var name = Name(s, ref pos);
                        string? value = null;
                        if (pos < s.Length && s[pos] == '=')
                        {
                            pos++;
                            if (pos < s.Length && s[pos] is '"' or '\'')
                            {
                                var quote = s[pos++];
                                var end = s.IndexOf(quote, pos);
                                if (end < 0)
                                {
                                    throw Invalid(s);
                                }

                                value = s[pos..end];
                                pos = end + 1;
                            }
                            else
                            {
                                var end = s.IndexOf(']', pos);
                                value = end < 0 ? throw Invalid(s) : s[pos..end];
                                pos = end;
                            }
                        }

                        if (pos >= s.Length || s[pos] != ']')
                        {
                            throw Invalid(s);
                        }

                        pos++;
                        c.attributes.Add((name, value));
                        break;
                    default:
                        if (pos != start)
                        {
                            throw Invalid(s);
                        }

                        c.tag = Name(s, ref pos);
                        break;
                }
            }

            return c;
        }

        public bool Matches(FakeDomHost.FakeNode node)
        {
            if (tag is not null && tag != "*" && !string.Equals(node.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var (name, value) in attributes)
            {
                if (!node.Attributes.TryGetValue(name, out var actual) || value is not null && actual != value)
                {
                    return false;
                }
            }

            if (classes.Count > 0)
            {
                var actual = node.Attributes.TryGetValue("class", out var cls) ? cls.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
                if (!classes.All(actual.Contains))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Name(string s, ref int pos)
        {
            var start = pos;
            while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] is '-' or '_' or '*' or ':'))
            {
                pos++;
            }

            return pos > start ? s[start..pos] : throw Invalid(s);
        }

        private static FormatException Invalid(string s) =>
            new($"Unsupported selector '{s}'. Use tag, #id, .class, [attr] or [attr=value], optionally separated by spaces.");
    }
}
