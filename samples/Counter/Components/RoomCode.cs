using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Counter.Components;

/// <summary>
/// The same UI as <c>RoomRazor.nwui</c>, written in C#: template components (Panel, Stepper) are plain
/// classes, props are object initializers and slots are <see cref="Slot"/>s.
/// </summary>
public sealed class RoomCode : Component
{
    private const int MaxArea = 200;

    private readonly Signal<int> width = new(3);
    private readonly Signal<int> length = new(4);
    private readonly RoomExtra[] extras = { new("Doors", 1), new("Windows", 2) };

    protected override Node Build() =>
        new Panel
        {
            Title = "C# (code)",
            Actions = new Slot(() => Button(Class("reset"), OnClick(Reset), "Reset")),
            ChildContent = new Slot(() => Div(Class("room"),
                new Stepper { Label = "Width", Value = width },
                new Stepper { Label = "Length", Value = length, Step = 2, Max = 20 },
                new ProgressBar { Value = new(() => width.Value * length.Value), Max = MaxArea },
                Ul(Class("extras"), Each(
                    () => extras,
                    e => e.Name,
                    e => Li(new Stepper { Label = e.Name, Value = e.Amount, Max = 3 }))),
                P(Class("summary"), Text(() => (width.Value * length.Value).ToString() + " m² with " + ExtrasTotal().ToString() + " extras")))),
        }.Render();

    private int ExtrasTotal()
    {
        var n = 0;
        foreach (var e in extras)
        {
            n += e.Amount.Value;
        }

        return n;
    }

    private void Reset()
    {
        width.Value = 3;
        length.Value = 4;
        extras[0].Amount.Value = 1;
        extras[1].Amount.Value = 2;
    }
}
