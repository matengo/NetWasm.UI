using System.Collections.Generic;

namespace NetWasm.UI.Generator;

/// <summary>Raised by the parser/emitter; becomes a diagnostic pointing into the template.</summary>
internal sealed class TemplateException : System.Exception
{
    public TemplateException(string id, string message, int start, int length = 1)
        : base(message)
    {
        Id = id;
        Start = start;
        Length = length;
    }

    public string Id { get; }
    public int Start { get; }
    public int Length { get; }
}

internal sealed class TemplateFile
{
    public List<Directive> Usings { get; } = new();
    public Directive? Namespace { get; set; }
    public List<Directive> CodeBlocks { get; } = new();
    public List<PropDecl> Props { get; } = new();
    public List<TNode> Nodes { get; } = new();
}

/// <summary><c>@prop Type Name = default;</c></summary>
internal sealed class PropDecl
{
    public PropDecl(Directive type, Directive name, Directive? defaultValue)
    {
        Type = type;
        Name = name;
        Default = defaultValue;
    }

    public Directive Type { get; }
    public Directive Name { get; }
    public Directive? Default { get; }
}

/// <summary>A piece of user C# with its offset in the template (for #line mapping).</summary>
internal sealed class Directive
{
    public Directive(string code, int start)
    {
        Code = code;
        Start = start;
    }

    public string Code { get; }
    public int Start { get; }
}

internal abstract class TNode
{
    public int Start { get; set; }
}

internal sealed class TText : TNode
{
    public TText(string raw) => Raw = raw;
    public string Raw { get; }
}

/// <summary><c>@expr</c> / <c>@(expr)</c> in content: reactive text.</summary>
internal sealed class TExpr : TNode
{
    public TExpr(Directive code) => Code = code;
    public Directive Code { get; }
}

/// <summary><c>@render(expr)</c>: any Content-convertible value, inserted as-is.</summary>
internal sealed class TRender : TNode
{
    public TRender(Directive code) => Code = code;
    public Directive Code { get; }
}

internal sealed class TElement : TNode
{
    public TElement(string tag) => Tag = tag;
    public string Tag { get; }
    public int TagStart { get; set; }
    public bool IsComponent => Tag.Length > 0 && char.IsUpper(Tag[0]);
    public List<TAttribute> Attributes { get; } = new();
    public List<TNode> Children { get; } = new();
}

internal sealed class TAttribute
{
    public TAttribute(string name, int start)
    {
        Name = name;
        Start = start;
    }

    public string Name { get; }
    public int Start { get; }
    public bool HasValue { get; set; }

    /// <summary>For ordinary attributes: literal text and <c>@expr</c> pieces.</summary>
    public List<Segment> Segments { get; } = new();

    /// <summary>For directive attributes (<c>@onclick</c>, <c>@bind</c>…): the C# value.</summary>
    public Directive? Code { get; set; }

    public bool IsDirective => Name.StartsWith("@", System.StringComparison.Ordinal);
}

internal sealed class Segment
{
    public Segment(bool isCode, string text, int start)
    {
        IsCode = isCode;
        Text = text;
        Start = start;
    }

    public bool IsCode { get; }
    public string Text { get; }
    public int Start { get; }
}

internal sealed class TIf : TNode
{
    public List<Branch> Branches { get; } = new();
}

internal sealed class Branch
{
    /// <summary>Null for the final <c>else</c>.</summary>
    public Directive? Condition { get; set; }
    public List<TNode> Body { get; } = new();
    public int Start { get; set; }
}

internal sealed class TForeach : TNode
{
    public string? Type { get; set; }
    public string Variable { get; set; } = string.Empty;
    public Directive Collection { get; set; } = null!;
    public List<TNode> Body { get; } = new();
}
