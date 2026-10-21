using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests.Templates;

/// <summary>A hand-written component with a reactive prop: templates pass it as a getter too.</summary>
public sealed class Chip : Component
{
    public Prop<string> Caption { get; set; } = "";

    protected override Node Build() => Span(Text(() => Caption.Value));
}
