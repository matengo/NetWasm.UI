using NetWasm.UI;
using static NetWasm.UI.Html;

namespace Counter;

public sealed class AppRoot : Component
{
    protected override Node Build() =>
        Main(
            H1("NetWasm.UI"),
            P(Class("tagline"), "Reflection-free, signal-driven UI compiled with NetWasm."),
            new CounterView(),
            new TodoList());
}
