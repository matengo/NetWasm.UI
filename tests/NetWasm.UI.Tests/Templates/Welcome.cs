using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests.Templates;

/// <summary>A hand-written component with constructor injection: a template tag fills the parameter with <c>Inject&lt;T&gt;()</c>.</summary>
public sealed class Welcome(IGreetings greetings) : Component
{
    public string Name { get; set; } = "";

    protected override Node Build() => Span(Text(() => $"{greetings.Hello} {Name} ({greetings.Count.Value})"));
}