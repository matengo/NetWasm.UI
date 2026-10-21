using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Counter;

public sealed class CounterView : Component
{
    private readonly Signal<int> count = new(0);

    protected override Node Build()
    {
        var doubled = new Computed<int>(() => count.Value * 2);

        return Section(
            Id("counter"),
            H2("Counter"),
            P("Count: ", Strong(Id("count"), Text(count)), " · doubled: ", Text(doubled)),
            Button(Id("inc"), OnClick(() => count.Value += 15), "+15"),
            Button(Id("dec"), OnClick(() => count.Value--), "-1"),
            Button(Id("reset"), Disabled(() => count.Value == 0), OnClick(() => count.Value = 0), "Reset"),
            When(() => count.Value >= 10, () => P(Class("hint"), "That's a lot of clicks!")));
    }
}
