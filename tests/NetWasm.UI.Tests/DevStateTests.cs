using NetWasm.UI.Reactive;

namespace NetWasm.UI.Tests;

public class DevStateTests : IDisposable
{
    public DevStateTests() => DevState.Reset();

    public void Dispose() => DevState.Reset();

    // Saves the state after `change`, then "reloads": a fresh DevState enabled with that state.
    private static void Reload(Action change)
    {
        change();
        var state = DevState.Serialize();
        DevState.Reset();
        DevState.Enable(state);
    }

    [Fact]
    public void Off_by_default()
    {
        Assert.Null(DevState.Tracker);
        var a = new Signal<int>(0);
        a.Value = 1;
        Assert.Equal("", DevState.Serialize());
    }

    [Fact]
    public void Restores_changed_int_bool_and_string_signals()
    {
        DevState.Enable("");
        var count = new Signal<int>(0);
        var on = new Signal<bool>(false);
        var name = new Signal<string>("");
        _ = new Signal<int>(5);
        Reload(() =>
        {
            count.Value = -42;
            on.Value = true;
            name.Value = "a;b:12\n";
        });

        Assert.Equal(-42, new Signal<int>(0).Value);
        Assert.True(new Signal<bool>(false).Value);
        Assert.Equal("a;b:12\n", new Signal<string>("").Value);
        Assert.Equal(5, new Signal<int>(5).Value);
    }

    [Fact]
    public void Int_extremes_round_trip()
    {
        DevState.Enable("");
        var min = new Signal<int>(0);
        var max = new Signal<int>(0);
        Reload(() =>
        {
            min.Value = int.MinValue;
            max.Value = int.MaxValue;
        });

        Assert.Equal(int.MinValue, new Signal<int>(0).Value);
        Assert.Equal(int.MaxValue, new Signal<int>(0).Value);
    }

    [Fact]
    public void Changed_initial_value_or_type_resets_the_signal()
    {
        DevState.Enable("");
        var a = new Signal<int>(1);
        var b = new Signal<int>(2);
        var c = new Signal<int>(3);
        Reload(() =>
        {
            a.Value = 10;
            b.Value = 20;
            c.Value = 30;
        });

        Assert.Equal(100, new Signal<int>(100).Value);
        Assert.Equal("2", new Signal<string>("2").Value);
        Assert.Equal(30, new Signal<int>(3).Value);
    }

    [Fact]
    public void Null_and_empty_strings_stay_distinct()
    {
        DevState.Enable("");
        var a = new Signal<string?>("x");
        var b = new Signal<string?>(null);
        Reload(() =>
        {
            a.Value = null;
            b.Value = "";
        });

        Assert.Null(new Signal<string?>("x").Value);
        Assert.Equal("", new Signal<string?>(null).Value);
    }

    [Fact]
    public void Other_types_are_ignored_but_keep_their_index()
    {
        DevState.Enable("");
        _ = new Signal<double>(1.5);
        var a = new Signal<int>(0);
        Reload(() => a.Value = 3);

        _ = new Signal<double>(1.5);
        Assert.Equal(3, new Signal<int>(0).Value);
    }

    [Theory]
    [InlineData("0;i1:05:9")]
    [InlineData("0;i1:01:x")]
    [InlineData("x;i1:01:9")]
    [InlineData("0;i")]
    public void Malformed_state_is_ignored(string state)
    {
        DevState.Enable(state);
        Assert.Equal(0, new Signal<int>(0).Value);
    }
}
