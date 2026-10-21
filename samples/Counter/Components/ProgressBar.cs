using NetWasm.UI;
using static NetWasm.UI.Html;

namespace Counter.Components;

/// <summary>A component written in C#. Its <see cref="Prop{T}"/> props are getters when a template passes <c>Value="@(…)"</c>.</summary>
public sealed class ProgressBar : Component
{
    public Prop<int> Value { get; set; } = 0;

    public Prop<int> Max { get; set; } = 100;

    protected override Node Build() =>
        Div(Class("progress"),
            Div(Class("progress-fill"), Style(() => $"width: {Percent()}%")),
            Span(Class("progress-text"), Text(() => $"{Value.Value} / {Max.Value}")));

    private int Percent()
    {
        var max = Max.Value;
        var p = max <= 0 ? 0 : Value.Value * 100 / max;
        return p < 0 ? 0 : p > 100 ? 100 : p;
    }
}
