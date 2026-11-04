using NetWasm.UI.Tests.Templates;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

internal interface IClockService
{
    string Now { get; }
}

internal sealed class FixedClock : IClockService
{
    public string Now => "12:00";
}

/// <summary>Constructed by the generated AddLazy interceptor: both parameters are injected.</summary>
internal sealed class Banner(IGreetings greetings, IClockService clock) : IDisposable
{
    public int Disposals { get; private set; }

    public string Text => $"{greetings.Hello} {clock.Now}";

    public void Dispose() => Disposals++;
}

public class GeneratedServicesTests : RendererTest
{
    [Fact]
    public void AddLazy_with_an_implementation_type_injects_its_constructor()
    {
        Banner? first = null;
        Banner? second = null;
        IClockService? clock = null;
        var services = new Services()
            .Add<IGreetings>(new Greetings())
            .AddLazy<IClockService, FixedClock>()
            .AddLazy<Banner>();

        App.Mount(new Lambda(() =>
        {
            first = Inject<Banner>();
            second = Inject<Banner>();
            clock = Inject<IClockService>();
            return P(first.Text);
        }), "#app", services);

        Assert.Equal("<p>Hej 12:00</p>", Dom.Html);
        Assert.Same(first, second);
        Assert.IsType<FixedClock>(clock);

        App.Unmount();
        Assert.Equal(1, first!.Disposals);
    }

    [Fact]
    public void Missing_constructor_dependency_throws_on_first_inject()
    {
        var services = new Services().AddLazy<Banner>();

        Assert.Throws<InvalidOperationException>(() =>
            App.Mount(new Lambda(() => P(Inject<Banner>().Text)), "#app", services));
    }

    private sealed class Greetings : IGreetings
    {
        public string Hello => "Hej";

        public NetWasm.UI.Reactive.Signal<int> Count { get; } = new(1);
    }

    private sealed class Lambda(Func<Node> build) : Component
    {
        protected override Node Build() => build();
    }
}