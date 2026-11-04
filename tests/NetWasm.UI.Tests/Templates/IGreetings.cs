using NetWasm.UI.Reactive;

namespace NetWasm.UI.Tests.Templates;

/// <summary>A service for <c>Greeter.nwui</c>'s <c>@inject</c>.</summary>
public interface IGreetings
{
    string Hello { get; }

    Signal<int> Count { get; }
}
