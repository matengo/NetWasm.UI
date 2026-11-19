using System.Text;
using NetWasm.UI.Dom;

namespace NetWasm.UI.Testing;

/// <summary>
/// Decodes render batches into an in-memory DOM so tests can assert on the resulting tree.
/// This is the reference decoder; dom.mjs implements the same format for the browser. Internal: it mirrors the op
/// protocol, which changes between releases. <see cref="TestRenderer"/> is the public surface over it.
/// </summary>
internal sealed class FakeDomHost : IDomHost, IDomQueries
{
    public sealed class FakeNode
    {
        public int Id { get; set; }
        public string Kind { get; init; } = "";   // element | text | anchor
        public string Tag { get; init; } = "";
        public string Text { get; set; } = "";
        public Dictionary<string, string> Attributes { get; } = new();
        public Dictionary<string, object> Properties { get; } = new();
        public Dictionary<int, (string Event, byte Flags)> Listeners { get; } = new();

        /// <summary>Listeners this node has through its template (Op.TemplateListener), with its row's first node id.</summary>
        public List<(string Event, int Slot, byte Flags, int First)> RowListeners { get; } = new();
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
    private readonly Dictionary<int, FakeTemplate> templates = new();
    private readonly Dictionary<int, List<(int Hole, string Event, int Slot, byte Flags)>> templateListeners = new();
    public FakeNode Document { get; } = new() { Id = 0, Kind = "element", Tag = "body" };
    public List<(string Op, string Detail)> Log { get; } = new();
    public int BatchCount { get; private set; }

    /// <summary>Document/window listeners by handler id.</summary>
    public Dictionary<int, (GlobalTarget Target, string Event, byte Flags, int WithinId)> GlobalListeners { get; } = new();

    /// <summary>Resize observations: handler id → node id.</summary>
    public Dictionary<int, int> ResizeObservers { get; } = new();

    /// <summary>Portal targets: node id → selector.</summary>
    public Dictionary<int, string> Portals { get; } = new();

    /// <summary>Answers for the <see cref="IDomQueries"/> reads.</summary>
    public Dictionary<int, Rect> Rects { get; } = new();

    public int ActiveId { get; set; }

    public Rect ViewportRect { get; set; } = new(0, 0, 1024, 768);

    public int Reads { get; private set; }

    public Rect GetRect(int id)
    {
        Reads++;
        return Rects.TryGetValue(id, out var r) ? r : default;
    }

    public int ActiveElementId()
    {
        Reads++;
        return ActiveId;
    }

    public bool Contains(int parentId, int childId)
    {
        Reads++;
        for (var n = Nodes.GetValueOrDefault(childId); n is not null; n = n.Parent)
        {
            if (n.Id == parentId && Nodes.ContainsKey(parentId) && ReferenceEquals(n, Nodes[parentId]))
            {
                return true;
            }
        }

        return false;
    }

    public Rect Viewport()
    {
        Reads++;
        return ViewportRect;
    }

    public FakeNode this[int id] => Nodes[id];

    public string Html => string.Concat(Document.Children.Select(c => c.ToHtml()));

    public void ApplyBatch(ReadOnlySpan<byte> batch)
    {
        // The span is only valid during the call (the renderer reuses its buffer), so copy it.
        var ops = batch.ToArray();
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
                    ExpectFree(id);
                    Nodes[id] = new FakeNode { Id = id, Kind = "element", Tag = tag };
                    Log.Add(("CreateElement", $"{id} {tag}"));
                    break;
                }
                case Op.CreateText:
                {
                    var id = ReadInt(ops, ref pos);
                    var text = ReadString(ops, ref pos);
                    ExpectFree(id);
                    Nodes[id] = new FakeNode { Id = id, Kind = "text", Text = text };
                    Log.Add(("CreateText", $"{id} '{text}'"));
                    break;
                }
                case Op.CreateAnchor:
                {
                    var id = ReadInt(ops, ref pos);
                    ExpectFree(id);
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
                case Op.SetTextInt:
                {
                    var id = ReadInt(ops, ref pos);
                    var text = ReadInt(ops, ref pos).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Nodes[id].Text = text;
                    Log.Add(("SetTextInt", $"{id} '{text}'"));
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
                    var parentNode = Nodes[parent];
                    if (parentNode.Kind == "anchor")
                    {
                        // Insertion point: like dom.mjs, appending to a comment inserts before it.
                        Attach(parentNode.Parent ?? throw new InvalidOperationException($"Anchor {parent} has no parent"), Nodes[child], parentNode);
                    }
                    else
                    {
                        Attach(parentNode, Nodes[child], null);
                    }

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
                case Op.RemoveAnimated:
                {
                    var id = ReadInt(ops, ref pos);
                    Detach(Nodes[id]);
                    Log.Add((op == Op.Remove ? "Remove" : "RemoveAnimated", $"{id}"));
                    break;
                }
                case Op.RemoveRange:
                {
                    var first = Nodes[ReadInt(ops, ref pos)];
                    var end = Nodes[ReadInt(ops, ref pos)];
                    var siblings = first.Parent!.Children;
                    var from = siblings.IndexOf(first);
                    var to = siblings.IndexOf(end);
                    if (from < 0 || to < from)
                    {
                        throw new InvalidOperationException($"RemoveRange {first.Id}..{end.Id}: end is not a following sibling");
                    }

                    foreach (var node in siblings.GetRange(from, to - from))
                    {
                        node.Parent = null;
                    }

                    siblings.RemoveRange(from, to - from);
                    Log.Add(("RemoveRange", $"{first.Id}..{end.Id}"));
                    break;
                }
                case Op.Release:
                {
                    var id = ReadInt(ops, ref pos);
                    Nodes.Remove(id);
                    Log.Add(("Release", $"{id}"));
                    break;
                }
                case Op.ReleaseBlock:
                {
                    var first = ReadInt(ops, ref pos);
                    var count = ReadInt(ops, ref pos);
                    for (var id = first; id < first + count; id++)
                    {
                        Nodes.Remove(id);
                    }

                    Log.Add(("ReleaseBlock", $"{first} {count}"));
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
                    ActiveId = id;
                    Log.Add(("Focus", $"{id}"));
                    break;
                }
                case Op.FocusEdge:
                {
                    var id = ReadInt(ops, ref pos);
                    var edge = ops[pos++] == 0 ? "first" : "last";
                    var tabbable = Nodes[id].Descendants().Where(IsTabbable);
                    ActiveId = (edge == "first" ? tabbable.FirstOrDefault() : tabbable.LastOrDefault())?.Id ?? id;
                    Log.Add(("FocusEdge", $"{id} {edge}"));
                    break;
                }
                case Op.Call:
                {
                    var id = ReadInt(ops, ref pos);
                    var method = ReadString(ops, ref pos);
                    var count = ops[pos++];
                    var args = new string[count];
                    for (var i = 0; i < count; i++)
                    {
                        args[i] = ReadString(ops, ref pos);
                    }

                    Log.Add(("Call", $"{id} {method}({string.Join(", ", args)})"));
                    break;
                }
                case Op.AddGlobalListener:
                {
                    var target = (GlobalTarget)ops[pos++];
                    var eventName = ReadString(ops, ref pos);
                    var handlerId = ReadInt(ops, ref pos);
                    var flags = ops[pos++];
                    var within = ReadInt(ops, ref pos);
                    GlobalListeners[handlerId] = (target, eventName, flags, within);
                    Log.Add(("AddGlobalListener", $"{target} {eventName} #{handlerId}" + (within != 0 ? $" within {within}" : "")));
                    break;
                }
                case Op.RemoveGlobalListener:
                {
                    var handlerId = ReadInt(ops, ref pos);
                    GlobalListeners.Remove(handlerId);
                    ResizeObservers.Remove(handlerId);
                    Log.Add(("RemoveGlobalListener", $"#{handlerId}"));
                    break;
                }
                case Op.ObserveResize:
                {
                    var id = ReadInt(ops, ref pos);
                    var handlerId = ReadInt(ops, ref pos);
                    ResizeObservers[handlerId] = id;
                    Log.Add(("ObserveResize", $"{id} #{handlerId}"));
                    break;
                }
                case Op.Portal:
                {
                    var id = ReadInt(ops, ref pos);
                    var selector = ReadString(ops, ref pos);
                    Attach(Document, Nodes[id], null);
                    Portals[id] = selector;
                    Log.Add(("Portal", $"{id} -> {selector}"));
                    break;
                }
                case Op.DefineTemplate:
                {
                    var id = ReadInt(ops, ref pos);
                    var html = ReadString(ops, ref pos);
                    templates[id] = FakeTemplate.Parse(html);
                    Log.Add(("DefineTemplate", $"{id} {html}"));
                    break;
                }
                case Op.TemplateListener:
                {
                    var id = ReadInt(ops, ref pos);
                    var hole = ReadInt(ops, ref pos);
                    var eventName = ReadString(ops, ref pos);
                    var slot = ops[pos++];
                    var flags = ops[pos++];
                    if (!Template.CanListen(eventName))
                    {
                        // As dom.mjs: a template listener is one of the events the host delegates.
                        throw new InvalidOperationException($"A template cannot listen for '{eventName}'.");
                    }

                    if (!templateListeners.TryGetValue(id, out var list))
                    {
                        templateListeners[id] = list = new();
                    }

                    list.Add((hole, eventName, slot, flags));
                    Log.Add(("TemplateListener", $"{id} hole {hole} {eventName} slot {slot}"));
                    break;
                }
                case Op.CloneTemplate:
                {
                    var id = ReadInt(ops, ref pos);
                    var first = ReadInt(ops, ref pos);
                    var holes = templates[id].Clone();
                    for (var i = 0; i < holes.Count; i++)
                    {
                        ExpectFree(first + i);
                        holes[i].Id = first + i;
                        Nodes[first + i] = holes[i];
                    }

                    if (templateListeners.TryGetValue(id, out var listeners))
                    {
                        foreach (var (hole, eventName, slot, flags) in listeners)
                        {
                            holes[hole].RowListeners.Add((eventName, slot, flags, first));
                        }
                    }

                    Log.Add(("CloneTemplate", $"{id} -> {first}"));
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unknown op {op} at {pos - 1}");
            }
        }
    }

    // As dom.mjs isTabbable, without layout: a focusable element that is not disabled, inert or tabindex="-1".
    private static bool IsTabbable(FakeNode n)
    {
        if (n.Kind != "element" || n.Id == 0 || IsSet(n, "disabled") || IsSet(n, "inert"))
        {
            return false;
        }

        if (n.Attributes.TryGetValue("tabindex", out var tabIndex))
        {
            return !tabIndex.StartsWith('-');
        }

        return n.Tag is "button" or "input" or "select" or "textarea" or "summary"
            || (n.Tag is "a" or "area") && n.Attributes.ContainsKey("href");
    }

    private static bool IsSet(FakeNode n, string name) =>
        n.Attributes.ContainsKey(name) || n.Properties.TryGetValue(name, out var v) && v is true;

    // The renderer reuses released node ids; a reused id must have been released first, as dom.mjs replaces the node
    // an id names.
    private void ExpectFree(int id)
    {
        if (Nodes.ContainsKey(id))
        {
            throw new InvalidOperationException($"Node id {id} is created while its node is still live.");
        }
    }

    /// <summary>
    /// Delivers an event to a node's template listener for <paramref name="eventName"/>, as dom.mjs does: to the
    /// node's row with the listener's slot.
    /// </summary>
    public void RowEvent(FakeNode node, string eventName, DomEvent? e = null)
    {
        var listener = node.RowListeners.First(l => l.Event == eventName);
        Renderer.DispatchRow(listener.First, listener.Slot, e ?? new DomEvent(eventName));
    }

    /// <summary>Finds the handler id of the first listener for <paramref name="eventName"/> on a node.</summary>
    public int HandlerFor(FakeNode node, string eventName) =>
        node.Listeners.First(l => l.Value.Event == eventName).Key;

    public FakeNode? Query(string tag, Func<FakeNode, bool>? predicate = null) =>
        Document.Descendants().FirstOrDefault(n => n.Kind == "element" && n.Tag == tag && (predicate is null || predicate(n)));

    public List<FakeNode> QueryAll(string tag, Func<FakeNode, bool>? predicate = null) =>
        Document.Descendants().Where(n => n.Kind == "element" && n.Tag == tag && (predicate is null || predicate(n))).ToList();

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
