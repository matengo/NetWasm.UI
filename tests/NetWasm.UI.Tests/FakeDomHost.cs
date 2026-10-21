using System.Text;
using NetWasm.UI.Dom;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NetWasm.UI.Tests;

/// <summary>
/// Decodes render batches into an in-memory DOM so tests can assert on the resulting tree.
/// This is the reference decoder; dom.mjs implements the same format for the browser.
/// </summary>
public sealed class FakeDomHost : IDomHost
{
    public sealed class FakeNode
    {
        public int Id { get; init; }
        public string Kind { get; init; } = "";   // element | text | anchor
        public string Tag { get; init; } = "";
        public string Text { get; set; } = "";
        public Dictionary<string, string> Attributes { get; } = new();
        public Dictionary<string, object> Properties { get; } = new();
        public Dictionary<int, (string Event, byte Flags)> Listeners { get; } = new();
        public List<FakeNode> Children { get; } = new();
        public FakeNode? Parent { get; set; }

        public IEnumerable<FakeNode> Descendants()
        {
            foreach (var child in Children)
            {
                yield return child;
                foreach (var d in child.Descendants())
                {
                    yield return d;
                }
            }
        }

        private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "input", "br", "hr", "img", "meta", "link",
        };

        /// <summary>Serializes to a compact HTML-like string (anchors rendered as &lt;!----&gt;).</summary>
        public string ToHtml()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        private void Write(StringBuilder sb)
        {
            switch (Kind)
            {
                case "text":
                    sb.Append(Text);
                    return;
                case "anchor":
                    sb.Append("<!---->");
                    return;
            }

            sb.Append('<').Append(Tag);
            foreach (var (name, value) in Attributes.OrderBy(a => a.Key))
            {
                sb.Append(' ').Append(name);
                if (value.Length > 0)
                {
                    sb.Append("=\"").Append(value).Append('"');
                }
            }

            sb.Append('>');
            if (VoidTags.Contains(Tag!))
            {
                return;
            }

            foreach (var child in Children)
            {
                child.Write(sb);
            }

            sb.Append("</").Append(Tag).Append('>');
        }
    }

    public Dictionary<int, FakeNode> Nodes { get; } = new();
    public FakeNode Document { get; } = new() { Id = 0, Kind = "element", Tag = "body" };
    public List<(string Op, string Detail)> Log { get; } = new();
    public int BatchCount { get; private set; }

    public FakeNode this[int id] => Nodes[id];

    public string Html => string.Concat(Document.Children.Select(c => c.ToHtml()));

    public void ApplyBatch(byte[] ops)
    {
        BatchCount++;
        var pos = 0;
        while (pos < ops.Length)
        {
            var op = (Op)ops[pos++];
            switch (op)
            {
                case Op.CreateElement:
                {
                    var id = ReadInt(ops, ref pos);
                    var tag = ReadString(ops, ref pos);
                    Nodes[id] = new FakeNode { Id = id, Kind = "element", Tag = tag };
                    Log.Add(("CreateElement", $"{id} {tag}"));
                    break;
                }
                case Op.CreateText:
                {
                    var id = ReadInt(ops, ref pos);
                    var text = ReadString(ops, ref pos);
                    Nodes[id] = new FakeNode { Id = id, Kind = "text", Text = text };
                    Log.Add(("CreateText", $"{id} '{text}'"));
                    break;
                }
                case Op.CreateAnchor:
                {
                    var id = ReadInt(ops, ref pos);
                    Nodes[id] = new FakeNode { Id = id, Kind = "anchor" };
                    Log.Add(("CreateAnchor", $"{id}"));
                    break;
                }
                case Op.SetText:
                {
                    var id = ReadInt(ops, ref pos);
                    var text = ReadString(ops, ref pos);
                    Nodes[id].Text = text;
                    Log.Add(("SetText", $"{id} '{text}'"));
                    break;
                }
                case Op.SetAttribute:
                {
                    var id = ReadInt(ops, ref pos);
                    var name = ReadString(ops, ref pos);
                    var value = ReadString(ops, ref pos);
                    Nodes[id].Attributes[name] = value;
                    Log.Add(("SetAttribute", $"{id} {name}='{value}'"));
                    break;
                }
                case Op.RemoveAttribute:
                {
                    var id = ReadInt(ops, ref pos);
                    var name = ReadString(ops, ref pos);
                    Nodes[id].Attributes.Remove(name);
                    Log.Add(("RemoveAttribute", $"{id} {name}"));
                    break;
                }
                case Op.SetProperty:
                {
                    var id = ReadInt(ops, ref pos);
                    var name = ReadString(ops, ref pos);
                    var value = ReadString(ops, ref pos);
                    Nodes[id].Properties[name] = value;
                    Log.Add(("SetProperty", $"{id} {name}='{value}'"));
                    break;
                }
                case Op.SetBoolProperty:
                {
                    var id = ReadInt(ops, ref pos);
                    var name = ReadString(ops, ref pos);
                    var value = ops[pos++] != 0;
                    Nodes[id].Properties[name] = value;
                    Log.Add(("SetBoolProperty", $"{id} {name}={value}"));
                    break;
                }
                case Op.AppendChild:
                {
                    var parent = ReadInt(ops, ref pos);
                    var child = ReadInt(ops, ref pos);
                    Attach(Nodes[parent], Nodes[child], null);
                    Log.Add(("AppendChild", $"{parent} <- {child}"));
                    break;
                }
                case Op.InsertBefore:
                {
                    var reference = ReadInt(ops, ref pos);
                    var child = ReadInt(ops, ref pos);
                    var refNode = Nodes[reference];
                    Attach(refNode.Parent ?? throw new InvalidOperationException($"Reference {reference} has no parent"), Nodes[child], refNode);
                    Log.Add(("InsertBefore", $"{child} before {reference}"));
                    break;
                }
                case Op.Remove:
                {
                    var id = ReadInt(ops, ref pos);
                    Detach(Nodes[id]);
                    Log.Add(("Remove", $"{id}"));
                    break;
                }
                case Op.Release:
                {
                    var id = ReadInt(ops, ref pos);
                    Nodes.Remove(id);
                    Log.Add(("Release", $"{id}"));
                    break;
                }
                case Op.AddListener:
                {
                    var id = ReadInt(ops, ref pos);
                    var eventName = ReadString(ops, ref pos);
                    var handlerId = ReadInt(ops, ref pos);
                    var flags = ops[pos++];
                    Nodes[id].Listeners[handlerId] = (eventName, flags);
                    Log.Add(("AddListener", $"{id} {eventName} #{handlerId}"));
                    break;
                }
                case Op.RemoveListener:
                {
                    var id = ReadInt(ops, ref pos);
                    var handlerId = ReadInt(ops, ref pos);
                    Nodes[id].Listeners.Remove(handlerId);
                    Log.Add(("RemoveListener", $"{id} #{handlerId}"));
                    break;
                }
                case Op.Mount:
                {
                    var id = ReadInt(ops, ref pos);
                    var selector = ReadString(ops, ref pos);
                    Attach(Document, Nodes[id], null);
                    Log.Add(("Mount", $"{id} -> {selector}"));
                    break;
                }
                case Op.Focus:
                {
                    var id = ReadInt(ops, ref pos);
                    Log.Add(("Focus", $"{id}"));
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unknown op {op} at {pos - 1}");
            }
        }
    }

    /// <summary>Finds the handler id of the first listener for <paramref name="eventName"/> on a node.</summary>
    public int HandlerFor(FakeNode node, string eventName) =>
        node.Listeners.First(l => l.Value.Event == eventName).Key;

    public FakeNode? Query(string tag, Func<FakeNode, bool>? predicate = null) =>
        Document.Descendants().FirstOrDefault(n => n.Kind == "element" && n.Tag == tag && (predicate is null || predicate(n)));

    private static void Attach(FakeNode parent, FakeNode child, FakeNode? before)
    {
        Detach(child);
        child.Parent = parent;
        if (before is null)
        {
            parent.Children.Add(child);
        }
        else
        {
            parent.Children.Insert(parent.Children.IndexOf(before), child);
        }
    }

    private static void Detach(FakeNode node)
    {
        node.Parent?.Children.Remove(node);
        node.Parent = null;
    }

    private static int ReadInt(byte[] b, ref int pos)
    {
        var v = b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24);
        pos += 4;
        return v;
    }

    private static string ReadString(byte[] b, ref int pos)
    {
        var len = ReadInt(b, ref pos);
        var s = Encoding.UTF8.GetString(b, pos, len);
        pos += len;
        return s;
    }
}
